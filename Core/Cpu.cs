namespace misc64.Core;

/// <summary>Состояние CPU и пошаговое выполнение команд виртуальной машины</summary>
public sealed class Cpu
{
	// В каждом элементе хранится 64-битное значение одного регистра
	// Номер регистра из команды используется как индекс массива: номер 0 - Registers[0]
	public ulong[] Registers = new ulong[16];

	// Program Counter - адрес команды, которую CPU будет читать следующей
	// Команда занимает 4 байта, поэтому обычно PC увеличивается на 4
	public int PC;

	// Показывает, выполняется ли VM, штатно остановлена или находится в Panic
	public VmState State = VmState.Running;

	// В Memory находятся отдельные массивы байтов для команд Code и данных Data
	public Memory Memory = new Memory();

	// CALL складывает сюда адрес возврата, RET забирает его обратно
	// Это отдельный стек адресов возврата CPU
	private Stack<int> CallStack = new Stack<int>();

	/// <summary>Подготавливает CPU к запуску новой программы</summary>
	public void Init()
	{
		// Начинаем читать команды с самого начала массива Code
		PC = 0;

		// Переводим VM в состояние выполнения и забываем старые адреса возврата
		State = VmState.Running;
		CallStack.Clear();

		// Обнуляем все регистры, чтобы значения предыдущего запуска не сохранились
		for (int i = 0; i < Registers.Length; i++)
			Registers[i] = 0;
	}

	/// <summary>Читает и выполняет одну машинную команду</summary>
	public void Step()
	{
		// Остановленный CPU нельзя продолжать выполнять случайным повторным вызовом Step
		if (State != VmState.Running)
			throw new CpuException(CpuError.StepAfterHalt, "Нельзя выполнить команду: виртуальная машина уже остановлена.", (uint)PC);

		// FETCH + DECODE: читаем четыре байта из Code по адресу PC и разбираем их на поля
		DecodedInstruction Command = Decoder.Decode(Memory.CodeReadOut(PC));

		// Сначала предполагаем, что следующая команда стоит сразу после текущей
		// Переходы и RET могут заменить это значение адресом перехода/возврата
		int nextPC = PC + 4;
		int shift;
		long address;
		long signedRs1;
		long signedRs2;

		switch (Command.Opcode)
		{
			case Opcode.Nop:
				// Пустая команда ничего не меняет; ниже PC перейдёт к следующей
				break;

			case Opcode.Halt:
				// Штатная остановка. PC оставляем на адресе HALT
				State = VmState.Halted;
				return;

			case Opcode.Hcf:
				// Аварийная остановка VM. Сохраняем Panic и сообщаем причину вызывающему коду
				State = VmState.Panic;
				throw new CpuException(CpuError.HaltAndCatchFire, "Выполнена команда HCF: Kernel Panic.", (uint)PC);

			case Opcode.Ret:
				// Возврат возможен только если CALL раньше положил адрес в стек
				if (CallStack.Count == 0)
					throw new CpuException(CpuError.StackUnderflow, "RET вызван при пустом стеке адресов возврата.", (uint)PC);
				PC = CallStack.Pop();
				return;

			case Opcode.Ldi:
				// Загружаем immediate в регистр назначения
				// unchecked сохраняет 64-битный шаблон дополнительного кода для отрицательных чисел
				Registers[Command.Rd] = unchecked((ulong)Command.Immediate);
				break;

			case Opcode.Mov:
				// Копируем значение исходного регистра в регистр назначения
				Registers[Command.Rd] = Registers[Command.Rs1];
				break;

			case Opcode.Load:
				// Вычисляем байтовый адрес: значение базового регистра плюс signed-смещение команды
				address = (long)Registers[Command.Rs1] + Command.Immediate;
				if (address < int.MinValue || address > int.MaxValue)
					throw new CpuException(CpuError.MemoryAccess, "Адрес LOAD вне диапазона адресов памяти.", (uint)PC);
				// Читаем 8 байт из Data и помещаем полученное значение в регистр назначения
				Registers[Command.Rd] = Memory.DataReadOut((int)address);
				break;

			case Opcode.Store:
				// Адрес STORE считается так же, как у LOAD: базовый регистр + signed-смещение
				address = (long)Registers[Command.Rs1] + Command.Immediate;
				if (address < int.MinValue || address > int.MaxValue)
					throw new CpuException(CpuError.MemoryAccess, "Адрес STORE вне диапазона адресов памяти.", (uint)PC);
				// Записываем все 8 байт значения из rd в Data по вычисленному адресу
				Memory.DataWriteIn((int)address, Registers[Command.Rd]);
				break;

			case Opcode.Add:
				// Сложение по модулю 2^64: сохраняются младшие 64 бита результата
				Registers[Command.Rd] = unchecked(Registers[Command.Rs1] + Registers[Command.Rs2]);
				break;

			case Opcode.Sub:
				// Вычитание по модулю 2^64: сохраняются младшие 64 бита результата
				Registers[Command.Rd] = unchecked(Registers[Command.Rs1] - Registers[Command.Rs2]);
				break;

			case Opcode.Mul:
				// Умножение оставляет младшие 64 бита произведения
				Registers[Command.Rd] = unchecked(Registers[Command.Rs1] * Registers[Command.Rs2]);
				break;

			case Opcode.And:
				// Побитовое И: каждый бит результата равен 1, если оба входных бита равны 1
				Registers[Command.Rd] = Registers[Command.Rs1] & Registers[Command.Rs2];
				break;

			case Opcode.Or:
				// Побитовое ИЛИ: бит результата равен 1, если хотя бы один входной бит равен 1
				Registers[Command.Rd] = Registers[Command.Rs1] | Registers[Command.Rs2];
				break;

			case Opcode.Xor:
				// Исключающее ИЛИ: бит результата равен 1, если входные биты различаются
				Registers[Command.Rd] = Registers[Command.Rs1] ^ Registers[Command.Rs2];
				break;

			case Opcode.Not:
				// Инвертируем каждый бит первого исходного регистра
				Registers[Command.Rd] = ~Registers[Command.Rs1];
				break;

			case Opcode.Shl:
				// Берём только младшие 6 бит счётчика сдвига: для 64 бит это значения 0–63
				shift = (int)(Registers[Command.Rs2] & 0b11_1111);
				Registers[Command.Rd] = Registers[Command.Rs1] << shift;
				break;

			case Opcode.Shr:
				// Логический сдвиг вправо заполняет освободившиеся старшие биты нулями
				shift = (int)(Registers[Command.Rs2] & 0b11_1111);
				Registers[Command.Rd] = Registers[Command.Rs1] >> shift;
				break;

			case Opcode.Je:
				// При равных значениях регистров прыгаем от адреса следующей команды
				if (Registers[Command.Rs1] == Registers[Command.Rs2])
					nextPC = GetRelativeAddress(nextPC, Command.Immediate);
				break;

			case Opcode.Jne:
				// При разных значениях регистров прыгаем от адреса следующей команды
				if (Registers[Command.Rs1] != Registers[Command.Rs2])
					nextPC = GetRelativeAddress(nextPC, Command.Immediate);
				break;

			case Opcode.Jlt:
				// Сравниваем биты регистров как знаковые 64-битные числа в дополнительном коде
				signedRs1 = unchecked((long)Registers[Command.Rs1]);
				signedRs2 = unchecked((long)Registers[Command.Rs2]);
				if (signedRs1 < signedRs2)
					nextPC = GetRelativeAddress(nextPC, Command.Immediate);
				break;

			case Opcode.Jgt:
				// Аналогично JLT, только условие - первое число больше второго
				signedRs1 = unchecked((long)Registers[Command.Rs1]);
				signedRs2 = unchecked((long)Registers[Command.Rs2]);
				if (signedRs1 > signedRs2)
					nextPC = GetRelativeAddress(nextPC, Command.Immediate);
				break;

			case Opcode.Jmp:
				// Безусловный относительный переход; смещение задаётся в командах
				nextPC = GetRelativeAddress(nextPC, Command.Immediate);
				break;

			case Opcode.Call:
				// Сохраняем адрес команды после CALL, чтобы RET знал, куда вернуться
				if (CallStack.Count >= 8192)
					throw new CpuException(CpuError.StackOverflow, "Переполнен стек адресов возврата.", (uint)PC);
				CallStack.Push(nextPC);
				nextPC = GetRelativeAddress(nextPC, Command.Immediate);
				break;

			default:
				// Защита на случай, если в Opcode добавят значение без реализации здесь
				throw new CpuException(CpuError.InvalidInstruction, $"Неизвестная команда {Command.Opcode}.", (uint)PC);
		}

		// Большинство команд идут дальше по порядку; переходы уже подменили nextPC
		PC = nextPC;
	}

	/// <summary>Считает цель перехода от адреса следующей команды и проверяет её границы</summary>
	private int GetRelativeAddress(int addressAfterCommand, int wordOffset)
	{
		// Смещение измеряется в командах, поэтому переводим его в байты умножением на 4
		long targetAddress = (long)addressAfterCommand + ((long)wordOffset * 4);

		// Цель должна указывать на начало команды, которая целиком помещается в Code
		if (targetAddress < 0 || targetAddress > Memory.Code.Length - 4)
			throw new CpuException(CpuError.MemoryAccess, "Адрес перехода находится вне памяти команд.", (uint)PC);

		return (int)targetAddress;
	}
}

