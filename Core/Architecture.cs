namespace misc64.Core;

/// <summary>Команды собственной минимальной регистровой ISA</summary>
public enum Opcode : byte
{
	// Системные команды
	Nop     = 0x00,		// Пустая операция; остальные поля команды равны нулю (Simple)
	Halt    = 0x01,		// Штатная остановка виртуальной машины (Simple)
	Hcf     = 0x02,		// Halt and Catch Fire! Блокировка ядра (вечный цикл / прерывание / паника / смерть / кровь / бсод) (Simple)

	// Пересылка данных и память
	Mov     = 0x03,		// Копировать значение из rs1 в rd (R); другие формы MOV - псевдооперации ассемблера
	Ldi     = 0x04,		// Загрузить signed immediate в rd (I)
	Load    = 0x05,		// Загрузить 64-битное значение из памяти по base+offset в rd (I)
	Store   = 0x06,		// Записать значение rd в память по адресу base+offset (I)

	// Арифметика и логика: результат записывается в rd
	Add     = 0x07,		// rd = rs1 + rs2 (R); результат по модулю 2^64
	Sub     = 0x08,		// rd = rs1 - rs2 (R); результат по модулю 2^64
	Mul     = 0x09,		// rd = младшие 64 бита произведения rs1*rs2 (R)
	And     = 0x0A,		// Побитовое И: rd = rs1 & rs2 (R)
	Or      = 0x0B,		// Побитовое ИЛИ: rd = rs1 | rs2 (R)
	Xor     = 0x0C,		// Побитовое исключающее ИЛИ: rd = rs1 ^ rs2 (R)
	Not     = 0x0D,		// Побитовая инверсия: rd = ~rs1; rs2 равен нулю (R)
	Shl     = 0x0E,		// Логический сдвиг влево; число сдвига - младшие 6 бит rs2 (R)
	Shr     = 0x0F,		// Логический сдвиг вправо; число сдвига - младшие 6 бит rs2 (R)

	// Переходы сравнивают регистры напрямую; скрытые флаги не используются
	Je      = 0x10,		// Перейти, если rs1 == rs2 (B)
	Jne     = 0x11,		// Перейти, если rs1 != rs2 (B)
	Jlt     = 0x12,		// Signed-переход, если rs1 < rs2 (B)
	Jgt     = 0x13,		// Signed-переход, если rs1 > rs2 (B)
	Jmp     = 0x14,		// Безусловный относительный переход (J)
	Call    = 0x15,		// Сохранить адрес возврата и перейти относительно PC (J)
	Ret     = 0x16		// Вернуться по адресу со стека вызовов (Simple)
}

/// <summary>Формат размещения операндов в 32-битной машинной команде</summary>
public enum InstructionFormat
{
	Simple, 			// Только opcode, остальные биты зарезервированы
	R,      			// Opcode и три регистра: rd, rs1, rs2
	I,      			// Opcode, rd, базовый/исходный регистр и 14-битный immediate
	B,      			// Opcode, два регистра сравнения и смещение условного перехода
	J       			// Opcode и 26-битное смещение безусловного перехода/вызова
}

/// <summary>Категории ошибок виртуальной машины</summary>
public enum CpuError
{
	Assembly,			// Ошибка исходного текста ассемблера
	InvalidInstruction,	// Неизвестный opcode или неверно закодированные поля
	MemoryAccess,		// Недопустимый адрес или обращение за границу памяти
	StackOverflow,		// Переполнение стека вызовов
	StackUnderflow,		// Возврат при пустом стеке вызовов
	StepAfterHalt,		// Попытка продолжить после HALT
	StepLimit,			// Достигнут лимит команд в Run
	HaltAndCatchFire	// Выполнена команда HCF
}

/// <summary>Единая диагностическая ошибка ассемблера или процессора</summary>
public sealed class CpuException : Exception
{
    public CpuError Kind { get; }
    public uint? Pc { get; }
    public string? SourceName { get; }
    public int? LineNumber { get; }
    public string? SourceText { get; }

    public CpuException(CpuError kind, string message, uint? pc = null,
        string? sourceName = null, int? lineNumber = null, string? sourceText = null)
        : base(message)
    {
        Kind = kind;
        Pc = pc;
        SourceName = sourceName;
        LineNumber = lineNumber;
        SourceText = sourceText;
    }

    public override string ToString()
    {
        string location = SourceName is null ? "" : $"{SourceName}:{LineNumber}: ";
        string line = SourceText is null ? "" : $"\n    {SourceText.Trim()}";
        string pc = Pc is null ? "" : $" [PC=0x{Pc:X8}]";
        return $"{location}{Message}{pc}{line}";
    }
}

/// <summary>Поля команды, извлечённые декодером. Неиспользуемые поля равны нулю</summary>
public readonly record struct DecodedInstruction(
	Opcode Opcode,
	InstructionFormat Format,
	int Rd 	= 0,
	int Rs1 = 0,
	int Rs2 = 0,
	int Immediate = 0);
