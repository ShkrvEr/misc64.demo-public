namespace misc64.Core;

/// <summary>
/// Собирает opcode и поля операндов в одно 32-битное машинное слово
/// Decoder делает обратное: читает это слово и достаёт из него поля
/// </summary>
public static class Encoder
{
	/// <summary>
	/// Кодирует команду без операндов, например NOP, HALT, HCF или RET
	/// После opcode оставшиеся 26 бит остаются нулевыми
	/// </summary>
	public static uint EncodeSimple(Opcode opcode)
	{
		// Не даём случайно вызвать этот метод для команды другого формата
		RequireFormat(opcode, InstructionFormat.Simple);

		// Opcode занимает старшие 6 бит. Сдвиг влево на 26 ставит его в биты 31–26
		return (uint)opcode << 26;
	}

	/// <summary>
	/// Кодирует команду с регистрами: rd, rs1 и rs2
	/// Пример: ADD R1, R2, R3 означает rd=1, rs1=2, rs2=3
	/// </summary>
	public static uint EncodeR(Opcode opcode, int rd, int rs1, int rs2 = 0)
	{
		// Проверяем, что выбранная команда действительно использует R-формат
		RequireFormat(opcode, InstructionFormat.R);

		// Каждое поле регистра занимает 4 бита, поэтому допустимы номера 0–15
		CheckRegister(rd, nameof(rd));
		CheckRegister(rs1, nameof(rs1));
		CheckRegister(rs2, nameof(rs2));

		// MOV и NOT используют только rd и rs1. Для них rs2 обязан быть нулём
		if ((opcode is Opcode.Mov or Opcode.Not) && rs2 != 0)
			throw new ArgumentException("This instruction does not use rs2.", nameof(rs2));

		// Ставим каждое значение в его участок слова:
		// opcode - биты 31–26, rd - 25–22, rs1 - 21–18, rs2 - 17–14
		// Младшие 14 бит не используются и остаются нулевыми
		return ((uint)opcode << 26)
			 | ((uint)rd << 22)
			 | ((uint)rs1 << 18)
			 | ((uint)rs2 << 14);
	}

	/// <summary>
	/// Кодирует команду с регистром и 14-битным signed immediate
	/// Immediate должен быть в диапазоне от -8192 до 8191
	/// </summary>
	public static uint EncodeI(Opcode opcode, int rd, int rs1, int immediate)
	{
		if (opcode == Opcode.Ldi && rs1 != 0)
			throw new CpuException(CpuError.InvalidInstruction, $"LDI передан с ненулем регистром. Ожидалось rs1 = 0, но получено rs1 = {rs1}.");

		RequireFormat(opcode, InstructionFormat.I);
		CheckRegister(rd, nameof(rd));
		CheckRegister(rs1, nameof(rs1));
		CheckSigned(immediate, 14, nameof(immediate));

		// В I-формате rd занимает биты 25–22, rs1 - 21–18
		// Immediate занимает биты 17–4, поэтому сдвигается влево на 4
		// Последние 4 бита зарезервированы и остаются нулевыми
		// Маска 0x3FFF оставляет ровно 14 бит immediate в дополнительном коде
		return ((uint)opcode << 26)
			 | ((uint)rd << 22)
			 | ((uint)rs1 << 18)
			 | (((uint)immediate & 0x3FFF) << 4);
	}

	/// <summary>
	/// Кодирует условный переход, который сравнивает два регистра
	/// Смещение задаётся в командах относительно следующей команды
	/// </summary>
	public static uint EncodeB(Opcode opcode, int rs1, int rs2, int wordOffset)
	{
		RequireFormat(opcode, InstructionFormat.B);
		CheckRegister(rs1, nameof(rs1));
		CheckRegister(rs2, nameof(rs2));
		CheckSigned(wordOffset, 14, nameof(wordOffset));

		// В B-формате rs1 и rs2 занимают биты 25–22 и 21–18
		// Смещение занимает биты 17–4; младшие 4 бита оставлены нулевыми
		return ((uint)opcode << 26)
			 | ((uint)rs1 << 22)
			 | ((uint)rs2 << 18)
			 | (((uint)wordOffset & 0b11_1111_1111_1111) << 4);
	}

	/// <summary>
	/// Кодирует безусловный переход или вызов функции
	/// В J-формате используется signed смещение в командах шириной 26 бит
	/// </summary>
	public static uint EncodeJ(Opcode opcode, int wordOffset)
	{
		RequireFormat(opcode, InstructionFormat.J);
		CheckSigned(wordOffset, 26, nameof(wordOffset));

		// Opcode записывается в старшие 6 бит, смещение - в младшие 26 бит
		return ((uint)opcode << 26) | ((uint)wordOffset & 0x03FF_FFFF);
	}

	/// <summary>Проверяет, что opcode существует и у него ожидаемый формат</summary>
	private static void RequireFormat(Opcode opcode, InstructionFormat expected)
	{
		if (!Enum.IsDefined(typeof(Opcode), opcode) || Decoder.FormatOf(opcode) != expected)
			throw new ArgumentException($"Opcode {opcode} is not in {expected} format.", nameof(opcode));
	}

	/// <summary>Проверяет, что номер регистра помещается в поле шириной 4 бита</summary>
	private static void CheckRegister(int register, string name)
	{
		if (register is < 0 or > 15)
			throw new ArgumentOutOfRangeException(name, "Register must be between R0 and R15.");
	}

	/// <summary>Проверяет, что signed-значение помещается в заданное число бит</summary>
	private static void CheckSigned(int value, int bits, string name)
	{
		// Для signed-поля из N бит диапазон равен -2^(N-1)…2^(N-1)-1
		int min = -(1 << (bits - 1));
		int max = (1 << (bits - 1)) - 1;

		if (value < min || value > max)
			throw new ArgumentOutOfRangeException(name, $"Value does not fit in signed {bits} bits.");
	}
}
