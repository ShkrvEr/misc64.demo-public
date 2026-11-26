using misc64.Core;

namespace misc64.Assembler;

/// <summary>Читает asm-файл в два прохода и превращает команды в машинные слова</summary>
public class Assembler
{
	// Метка с индексом i и её адрес хранятся в ячейках с одинаковым индексом
	private string[] labelNames = new string[100];
	private int[] labelAddresses = new int[100];
	private int labelCount;
	private int commandCount;

	public uint[] AssembleFile(string fileName)
	{
		string[] lines = File.ReadAllLines(fileName);

		// Первый проход заранее запоминает все метки
		FirstRun(lines);

		// Временный вывод таблицы меток оставлен для проверки первого прохода
		for (int i = 0; i < labelCount; i++)
			Console.WriteLine($"{labelNames[i]} = {labelAddresses[i]}");

		// Во втором проходе кодируем команды и теперь уже можем находить метки
		return SecondRun(lines);
	}

	private void FirstRun(string[] lines)
	{
		int address = 0;
		labelCount = 0;
		commandCount = 0;

		for (int i = 0; i < lines.Length; i++)
		{
			string line = RemoveComment(lines[i]).Trim();
			if (line == "")
				continue;

			if (line.EndsWith(":"))
			{
				if (labelCount >= labelNames.Length)
					throw new FormatException("В программе слишком много меток (максимум 100).");

				string labelName = line.Substring(0, line.Length - 1).Trim();				// Убираем двоеточие с конца имени
				if (labelName == "")
					throw new FormatException("Имя метки не может быть пустым.");

				labelNames[labelCount] = labelName.ToUpper();
				labelAddresses[labelCount] = address;
				labelCount++;
			}
			else
			{
				// Каждая команда занимает 4 байта
				address += 4;
				commandCount++;
			}
		}
	}

	private uint[] SecondRun(string[] lines)
	{
		uint[] machineCode = new uint[commandCount];	// По одному слову на каждую команду из исходного файла
		int commandIndex = 0;
		int address = 0;

		for (int i = 0; i < lines.Length; i++)
		{
			string line = RemoveComment(lines[i]).Trim();
			if (line == "" || line.EndsWith(":"))
				continue;

			// Убираем запятые и делаем табы пробелами, чтобы разделить операнды
			line = line.Replace(",", " ");
			line = line.Replace('\t', ' ');
			string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string op = parts[0].ToUpper();
			string[] args = new string[parts.Length - 1];

			for (int j = 1; j < parts.Length; j++)
				args[j - 1] = parts[j];

			machineCode[commandIndex] = EncodeInstruction(op, args, address);
			commandIndex++;
			address += 4;
		}

		return machineCode;
	}

	private uint EncodeInstruction(string op, string[] args, int address)
	{
		// Сначала узнаём числовой код команды по её текстовому имени
		Opcode opcode = ParseOpcode(op);

		// Затем выбираем обработчик по формату команды, как он задан в Decoder/Architecture
		InstructionFormat format = Decoder.FormatOf(opcode);
		switch (format)
		{
			case InstructionFormat.Simple:
				return EncodeSimple(opcode, args);
			case InstructionFormat.R:
				return EncodeR(opcode, args);
			case InstructionFormat.I:
				return EncodeI(opcode, args);
			case InstructionFormat.B:
				return EncodeB(opcode, args, address);
			case InstructionFormat.J:
				return EncodeJ(opcode, args, address);
			default:
				throw new FormatException($"Для команды '{op}' не задан обработчик формата.");
		}
	}

	private Opcode ParseOpcode(string op)
	{
		// Здесь перечислены только команды, реально объявленные в Architecture.cs
		return op switch
		{
			"NOP"	=> Opcode.Nop,
			"HALT"	=> Opcode.Halt,
			"HCF"	=> Opcode.Hcf,
			"RET"	=> Opcode.Ret,
			"LDI"	=> Opcode.Ldi,
			"MOV"	=> Opcode.Mov,
			"LOAD"	=> Opcode.Load,
			"STORE"	=> Opcode.Store,
			"ADD"	=> Opcode.Add,
			"SUB"	=> Opcode.Sub,
			"MUL"	=> Opcode.Mul,
			"AND"	=> Opcode.And,
			"OR"	=> Opcode.Or,
			"XOR"	=> Opcode.Xor,
			"NOT"	=> Opcode.Not,
			"SHL"	=> Opcode.Shl,
			"SHR"	=> Opcode.Shr,
			"JE"	=> Opcode.Je,
			"JNE"	=> Opcode.Jne,
			"JLT"	=> Opcode.Jlt,
			"JGT"	=> Opcode.Jgt,
			"JMP"	=> Opcode.Jmp,
			"CALL"	=> Opcode.Call,
			_ 		=> throw new FormatException($"Неизвестная команда '{op}'.")
		};
	}

	private uint EncodeSimple(Opcode opcode, string[] args)
	{
		if (args.Length != 0)
			throw new FormatException($"Команда {opcode.ToString().ToUpper()} не принимает операндов.");

		return Encoder.EncodeSimple(opcode);
	}

	private uint EncodeR(Opcode opcode, string[] args)
	{
		// MOV и NOT используют два регистра; арифметические команды - три
		if (opcode == Opcode.Mov || opcode == Opcode.Not)
		{
			if (args.Length != 2)
				throw new FormatException($"Для {opcode.ToString().ToUpper()} нужны регистр-приёмник и регистр-источник.");

			int rd = ParseRegister(args[0]);
			int rs1 = ParseRegister(args[1]);
			return Encoder.EncodeR(opcode, rd, rs1);
		}

		if (args.Length != 3)
			throw new FormatException($"Для {opcode.ToString().ToUpper()} нужны регистр-приёмник и два регистра-источника.");

		int destination = ParseRegister(args[0]);
		int source1 = ParseRegister(args[1]);
		int source2 = ParseRegister(args[2]);
		return Encoder.EncodeR(opcode, destination, source1, source2);
	}

	private uint EncodeI(Opcode opcode, string[] args)
	{
		// I-формат общий, но LDI, LOAD и STORE по-разному используют его поля
		if (opcode == Opcode.Ldi)
		{
			if (args.Length != 2)
				throw new FormatException("Формат LDI: LDI регистр, число (например, LDI R1, 5).");

			int rd = ParseRegister(args[0]);
			int immediate = int.Parse(args[1]);
			return Encoder.EncodeI(opcode, rd, rs1: 0, immediate);
		}

		if (opcode == Opcode.Load || opcode == Opcode.Store)
		{
			if (args.Length != 3)
				throw new FormatException($"Формат {opcode.ToString().ToUpper()}: регистр значения, базовый регистр, смещение.");

			int register = ParseRegister(args[0]);
			int baseRegister = ParseRegister(args[1]);
			int offset = int.Parse(args[2]);
			return Encoder.EncodeI(opcode, register, baseRegister, offset);
		}

		throw new FormatException($"Для команды {opcode} не описаны операнды I-формата.");
	}

	private uint EncodeB(Opcode opcode, string[] args, int address)
	{
		if (args.Length != 3)
			throw new FormatException($"Для {opcode.ToString().ToUpper()} нужны два регистра и метка или смещение.");

		int rs1 = ParseRegister(args[0]);
		int rs2 = ParseRegister(args[1]);
		int wordOffset = ParseWordOffset(args[2], address);
		return Encoder.EncodeB(opcode, rs1, rs2, wordOffset);
	}

	private uint EncodeJ(Opcode opcode, string[] args, int address)
	{
		if (args.Length != 1)
			throw new FormatException($"Для {opcode.ToString().ToUpper()} нужна метка или смещение.");

		int wordOffset = ParseWordOffset(args[0], address);
		return Encoder.EncodeJ(opcode, wordOffset);
	}

	private int ParseWordOffset(string target, int address)
	{
		// Число трактуем как уже готовое смещение в командах
		int numericOffset;
		if (int.TryParse(target, out numericOffset))
			return numericOffset;

		// Если это не число, ищем имя метки, сохранённое первым проходом
		string targetName = target.ToUpper();
		for (int i = 0; i < labelCount; i++)
		{
			if (labelNames[i] == targetName)
			{
				// Адрес цели минус адрес следующей команды, затем переводим байты в команды
				int distanceInBytes = labelAddresses[i] - (address + 4);
				if (distanceInBytes % 4 != 0)
					throw new FormatException($"Метка '{target}' не выровнена по границе команды.");
				return distanceInBytes / 4;
			}
		}

		throw new FormatException($"Неизвестная метка '{target}'.");
	}

	private int ParseRegister(string registerName)
	{
		string name = registerName.ToUpper();

		// Регистр записывается только номером: R0, R1, ... R15
		if (name.Length >= 2 && name[0] == 'R')
		{
			int registerNumber;
			if (int.TryParse(name.Substring(1), out registerNumber) && registerNumber >= 0 && registerNumber <= 15)
				return registerNumber;
		}

		throw new FormatException($"Неизвестный регистр '{registerName}'.");
	}

	private string RemoveComment(string line)
	{
		int commentPosition = line.IndexOf(';');
		if (commentPosition >= 0)
			return line.Substring(0, commentPosition);
		return line;
	}
}


