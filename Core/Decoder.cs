namespace misc64.Core;

/// <summary>
/// Превращает одно 32-битное машинное слово в поля команды
/// Например, из числа достаёт opcode, номера регистров и immediate
/// 
/// R: [31:26] opcode:6 [25:22] rd:4 [21:18] rs1:4 [17:14] rs2:4 [13:0]
/// I: [31:26] opcode:6 [25:22] rd:4 [21:18] base/rs1:4 [17:4] signed immediate:14 [3:0]
/// B: [31:26] opcode:6 [25:22] rs1:4 [21:18] rs2:4 [17:4] signed word offset:14 [3:0]
/// J: [31:26] opcode:6 [25:0] signed word offset:26
/// S: [31:26] opcode:6 [25:0]
/// </summary>
public static class Decoder
{
	
	private const uint OpcodeMask 			= 0b11_1111; 							// Маска нужна, чтобы после сдвига взять ровно биты opcode
	private const uint RegisterMask 		= 0b1111; 								// Маска номера регистра занимает 4 бита
	private const uint SimpleReservedMask 	= 0b11_1111_1111_1111_1111_1111_1111; 	// В Simple-команде после opcode все 26 бит зарезервированы и должны быть нулями. Маска проверяет биты с 25 по 0
	private const uint RReservedMask 		= 0b11_1111_1111_1111; 					// В R-команде последние 14 бит зарезервированы и должны быть нулями. Поле rs2 находится выше них, в битах 17–14
	private const uint ImmediateMask 		= 0b11_1111_1111_1111; 					// Immediate в I-формате занимает 14 бит
	private const uint BranchImmediateMask 	= 0b11_1111_1111_1111; 					// Смещение в B-формате тоже занимает 14 бит
	private const uint IReservedMask 		= 0b1111; 								// В I- и B-форматах последние четыре бита зарезервированы и должны быть нулями

	/// <summary>
	/// Разбирает машинное слово: сначала определяет команду, затем извлекает поля
	/// согласно формату этой команды. Если opcode неизвестен или reserved-биты
	/// не равны нулю, выбрасывает ошибку
	/// </summary>
	public static DecodedInstruction Decode(uint word)
	{
		// Opcode занимает старшие 6 бит, то есть биты 31–26
		// Сдвигаем их вправо на 26 позиций, а маской отбрасываем всё лишнее
		byte opcodeValue = (byte)((word >> 26) & OpcodeMask);

		// Приведение числа к enum само по себе не проверяет, существует ли команда
		// Поэтому сначала явно проверяем, что такой код объявлен в Opcode
		if (!Enum.IsDefined(typeof(Opcode), opcodeValue))
		{
			throw new ArgumentException($"Unknown opcode 0x{opcodeValue:X2}.", nameof(word));
		}

		// Теперь код точно есть в перечислении, поэтому можно получить имя команды
		Opcode opcode = (Opcode)opcodeValue;

		// Формат определяет, как трактовать оставшиеся 26 бит команды
		InstructionFormat format = FormatOf(opcode);

		// Изначально читаем поля регистров из общей раскладки:
		// rd находится в битах 25–22, rs1 - 21–18, rs2 - 17–14
		// Для некоторых форматов эти поля будут не нужны или имеют другой смысл
		int rd 	= (int)((word >> 22) & RegisterMask);
		int rs1 = (int)((word >> 18) & RegisterMask);
		int rs2 = (int)((word >> 14) & RegisterMask);
		int immediate;

		if (opcode == Opcode.Ldi && rs1 != 0)
			throw new CpuException(CpuError.InvalidInstruction, $"LDI передан с ненулем регистром. Ожидалось rs1 = 0, но получено rs1 = {rs1}.");

		switch (format)
		{
			case InstructionFormat.Simple:
				// У простой команды есть только opcode. Все остальные 26 бит должны быть нулями
				Require((word & SimpleReservedMask) == 0, "Simple command has nonzero reserved bits.");

				// У этой команды нет регистров и immediate, поэтому возвращаем нули
				rd = rs1 = rs2 = 0;
				immediate = 0;
				break;

			case InstructionFormat.R:
				// В R-формате используются rd, rs1 и rs2
				// Нижние 14 бит не используются и обязаны быть нулевыми
				Require((word & RReservedMask) == 0, "R-format has nonzero reserved bits.");
				immediate = 0;
				break;

			case InstructionFormat.I:
				// В I-формате используются rd, rs1/base и 14-битное immediate
				// Самые младшие четыре бита оставлены в резерве
				Require((word & IReservedMask) == 0, "I-format has nonzero reserved bits.");

				// Immediate лежит в битах 17–4. Сдвигаем его на 4 вправо,
				// оставляем 14 бит и преобразуем из дополнительного кода в int
				immediate = SignExtend((word >> 4) & ImmediateMask, 14);

				// rs2 в I-формате не используется
				rs2 = 0;
				break;

			case InstructionFormat.B:
				// В B-формате rd отсутствует: поля 25–22 и 21–18 - это два
				// сравниваемых регистра. Смещение занимает биты 17–4
				// Младшие четыре зарезервированных бита должны быть нулевыми
				Require((word & IReservedMask) == 0, "B-format has nonzero reserved bits.");

				// Смещение знаковое и хранится в 14 битах
				immediate = SignExtend((word >> 4) & BranchImmediateMask, 14);
				rd = 0;
				rs2 = (int)((word >> 18) & RegisterMask);
				rs1 = (int)((word >> 22) & RegisterMask);
				break;

			case InstructionFormat.J:
				// В J-формате после opcode идёт одно 26-битное знаковое смещение
				immediate = SignExtend(word & SimpleReservedMask, 26);

				// Полей регистров у J-команды нет
				rd = rs1 = rs2 = 0;
				break;

			default:
				// Защита на случай, если формат добавят в enum, но забудут обработать здесь
				throw new ArgumentOutOfRangeException(nameof(format));
		}

		// Возвращаем команду и её уже извлечённые значения
		return new DecodedInstruction(opcode, format, rd, rs1, rs2, immediate);
	}

	/// <summary>
	/// Возвращает формат машинного слова для каждой команды
	/// Это правило должно совпадать с тем, как Encoder кодирует эту команду
	/// </summary>
	public static InstructionFormat FormatOf(Opcode opcode) => opcode switch
	{
		Opcode.Nop or Opcode.Halt or Opcode.Hcf or Opcode.Ret 								=> InstructionFormat.Simple,
		Opcode.Mov or Opcode.Add or Opcode.Sub or Opcode.Mul or Opcode.And or Opcode.Or
			or Opcode.Xor or Opcode.Not or Opcode.Shl or Opcode.Shr 						=> InstructionFormat.R,
		Opcode.Ldi or Opcode.Load or Opcode.Store 											=> InstructionFormat.I,
		Opcode.Je or Opcode.Jne or Opcode.Jlt or Opcode.Jgt 								=> InstructionFormat.B,
		Opcode.Jmp or Opcode.Call 															=> InstructionFormat.J,
		_ => throw new ArgumentOutOfRangeException(nameof(opcode), "Unknown opcode.")
	};

	/// <summary>
	/// Преобразует знаковое число из поля заданной ширины в обычный int
	/// Например, 14-битный шаблон 0b11_1111_1111_1111 означает -1,
	/// потому что старший бит поля равен 1
	/// </summary>
	private static int SignExtend(uint value, int bits)
	{
		// Номер знакового бита: для 14 бит это бит 13
		uint signBit = 1u << (bits - 1);

		// Маска оставляет только указанное число бит
		uint mask = (1u << bits) - 1;
		value &= mask;

		// Если знак отрицательный, арифметика ниже достраивает старшие единицы
		return unchecked((int)((value ^ signBit) - signBit));
	}

	/// <summary>Останавливает декодирование, если проверка поля не прошла</summary>
	private static void Require(bool condition, string message)
	{
		if (!condition)
		{
			throw new ArgumentException(message, "word");
		}
	}
}
