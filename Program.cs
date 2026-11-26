using misc64.Core;

// Отдельное окно только читает снимок
if (args.Length >= 2 && args[0] == "--watch")
{
	WatchPanel(args[1].ToLower());
	return;
}

// Это окно выполняет программу по шагам
if (args.Length >= 2 && args[0] == "--run")
{
	RunVm(args[1]);
	return;
}

// Если аргументов нет, запускаем проверки CPU и ассемблера
RunChecks();

static void RunVm(string sourceFile)
{
	Cpu cpu = LoadProgram(Path.GetFullPath(sourceFile), out uint[] program);
	string snapshotPath = Path.Combine(AppContext.BaseDirectory, "vm-snapshot.txt");
	string[] history = new string[8];
	int historyCount = 0;
	string lastError = "";

	WriteSnapshot(cpu, program, history, historyCount, lastError, snapshotPath);
	Console.WriteLine("VM запущена. В других окнах запусти режим --watch");

	while (cpu.State == VmState.Running)
	{
		Console.WriteLine($"PC = 0x{cpu.PC:X4}. Enter — следующий шаг, Q — остановить");
		if (Console.ReadKey(true).Key == ConsoleKey.Q)
			break;

		DecodedInstruction instruction = Decoder.Decode(cpu.Memory.CodeReadOut(cpu.PC));
		AddHistory(history, ref historyCount, $"0x{cpu.PC:X4} {FormatInstruction(instruction)}");

		try
		{
			cpu.Step();
		}
		catch (CpuException error)
		{
			lastError = error.Message;
		}

		WriteSnapshot(cpu, program, history, historyCount, lastError, snapshotPath);
	}

	WriteSnapshot(cpu, program, history, historyCount, lastError, snapshotPath);
	Console.WriteLine($"VM остановлена: {cpu.State}, PC = 0x{cpu.PC:X4}");
}

static void WriteSnapshot(Cpu cpu, uint[] program, string[] history, int historyCount, string lastError, string snapshotPath)
{
	string temporaryPath = snapshotPath + ".tmp";

	using (StreamWriter writer = new StreamWriter(temporaryPath, false))
	{
		writer.WriteLine("[registers]");
		writer.WriteLine($"State = {cpu.State}");
		writer.WriteLine($"PC = 0x{cpu.PC:X4}");
		for (int i = 0; i < cpu.Registers.Length; i++)
			writer.WriteLine($"R{i} = 0x{cpu.Registers[i]:X16} ({cpu.Registers[i]})");

		writer.WriteLine("[code]");
		for (int i = 0; i < program.Length; i++)
		{
			DecodedInstruction instruction = Decoder.Decode(program[i]);
			string marker = i * 4 == cpu.PC && cpu.State == VmState.Running ? ">" : " ";
			writer.WriteLine($"{marker} 0x{i * 4:X4} 0x{program[i]:X8} {FormatInstruction(instruction)}");
		}

		writer.WriteLine("[data]");
		for (int address = 0; address < 128; address += 8)
			writer.WriteLine($"0x{address:X4} = 0x{cpu.Memory.DataReadOut(address):X16}");

		writer.WriteLine("[history]");
		for (int i = 0; i < historyCount; i++)
			writer.WriteLine(history[i]);
		writer.WriteLine("[error]");
		writer.WriteLine(lastError);
	}

	File.Move(temporaryPath, snapshotPath, true);
}

static void WatchPanel(string panelName)
{
	string sectionName = "[" + panelName + "]";
	if (panelName != "registers" && panelName != "code" && panelName != "data" && panelName != "history")
	{
		Console.WriteLine("Окно должно быть registers, code, data или history");
		return;
	}

	string snapshotPath = Path.Combine(AppContext.BaseDirectory, "vm-snapshot.txt");
	while (true)
	{
		Console.Clear();
		Console.WriteLine("Окно VM: " + panelName);
		Console.WriteLine("Для выхода нажми Ctrl+C\n");

		try
		{
			string[] lines = File.ReadAllLines(snapshotPath);
			bool showLines = false;
			foreach (string line in lines)
			{
				if (line.StartsWith("["))
				{
					showLines = line == sectionName;
					continue;
				}
				if (showLines)
					Console.WriteLine(line);
			}
		}
		catch (IOException)
		{
			Console.WriteLine("Жду запуска VM в управляющем окне");
		}

		Thread.Sleep(250);
	}
}

static void AddHistory(string[] history, ref int historyCount, string entry)
{
	if (historyCount == history.Length)
	{
		for (int i = 1; i < history.Length; i++)
			history[i - 1] = history[i];
		historyCount--;
	}
	history[historyCount] = entry;
	historyCount++;
}

static string FormatInstruction(DecodedInstruction instruction)
{
	string op = instruction.Opcode.ToString().ToUpper();
	switch (instruction.Format)
	{
		case InstructionFormat.Simple:
			return op;
		case InstructionFormat.R:
			if (instruction.Opcode == Opcode.Mov || instruction.Opcode == Opcode.Not)
				return $"{op} R{instruction.Rd}, R{instruction.Rs1}";
			return $"{op} R{instruction.Rd}, R{instruction.Rs1}, R{instruction.Rs2}";
		case InstructionFormat.I:
			if (instruction.Opcode == Opcode.Ldi)
				return $"LDI R{instruction.Rd}, {instruction.Immediate}";
			return $"{op} R{instruction.Rd}, R{instruction.Rs1}, {instruction.Immediate}";
		case InstructionFormat.B:
			return $"{op} R{instruction.Rs1}, R{instruction.Rs2}, {instruction.Immediate}";
		case InstructionFormat.J:
			return $"{op} {instruction.Immediate}";
		default:
			return "unknown";
	}
}

static void RunChecks()
{
	Cpu opsCpu = RunProgram(TestFile("ops.asm"));
	Check("MOV", opsCpu.Registers[3], 10);
	Check("ADD", opsCpu.Registers[4], 13);
	Check("SUB", opsCpu.Registers[5], 7);
	Check("MUL", opsCpu.Registers[6], 30);
	Check("AND", opsCpu.Registers[7], 2);
	Check("OR", opsCpu.Registers[8], 11);
	Check("XOR", opsCpu.Registers[9], 9);
	Check("NOT", opsCpu.Registers[10], ~10UL);
	Check("SHL", opsCpu.Registers[11], 80);
	Check("SHR", opsCpu.Registers[12], 1);
	Check("STORE + LOAD", opsCpu.Registers[13], 30);
	Check("Данные в памяти", opsCpu.Memory.DataReadOut(100), 30);
	Check("HALT", (ulong)opsCpu.State, (ulong)VmState.Halted);

	Cpu branchesCpu = RunProgram(TestFile("branches.asm"));
	Check("Переходы пропустили команды", branchesCpu.Registers[3], 0);
	Check("Переходы дошли до HALT", (ulong)branchesCpu.State, (ulong)VmState.Halted);

	Cpu callCpu = RunProgram(TestFile("call.asm"));
	Check("CALL / RET", callCpu.Registers[2], 14);
	Check("CALL / RET дошли до HALT", (ulong)callCpu.State, (ulong)VmState.Halted);

	Cpu hcfCpu = LoadProgram(TestFile("hcf.asm"), out _);
	try
	{
		hcfCpu.Step();
		throw new Exception("HCF должен вызвать ошибку");
	}
	catch (CpuException error)
	{
		Check("HCF перевёл CPU в Panic", (ulong)hcfCpu.State, (ulong)VmState.Panic);
		Check("HCF вернул нужную ошибку", (ulong)error.Kind, (ulong)CpuError.HaltAndCatchFire);
	}

	CheckFormatError("Неизвестная команда", "unknown-command.asm");
	CheckFormatError("Неверное количество аргументов", "wrong-arguments.asm");
	CheckFormatError("Неизвестная метка", "unknown-label.asm");
	CheckRangeError("Immediate вне диапазона", "immediate-out-of-range.asm");
	Console.WriteLine("Все проверки завершены");
}

static string TestFile(string fileName)
{
	return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tests", fileName));
}

static Cpu RunProgram(string fileName)
{
	Cpu cpu = LoadProgram(fileName, out _);
	while (cpu.State == VmState.Running)
		cpu.Step();
	return cpu;
}

static Cpu LoadProgram(string fileName, out uint[] machineCode)
{
	Cpu cpu = new Cpu();
	cpu.Init();
	misc64.Assembler.Assembler assembler = new misc64.Assembler.Assembler();
	machineCode = assembler.AssembleFile(fileName);
	for (int i = 0; i < machineCode.Length; i++)
		cpu.Memory.CodeWriteIn(i * 4, machineCode[i]);
	return cpu;
}

static void CheckFormatError(string name, string fileName)
{
	try
	{
		misc64.Assembler.Assembler assembler = new misc64.Assembler.Assembler();
		assembler.AssembleFile(TestFile(fileName));
		throw new Exception($"{name}: ожидалась ошибка формата");
	}
	catch (FormatException)
	{
		Console.WriteLine($"PASS: {name}");
	}
}

static void CheckRangeError(string name, string fileName)
{
	try
	{
		misc64.Assembler.Assembler assembler = new misc64.Assembler.Assembler();
		assembler.AssembleFile(TestFile(fileName));
		throw new Exception($"{name}: ожидалась ошибка диапазона");
	}
	catch (ArgumentOutOfRangeException)
	{
		Console.WriteLine($"PASS: {name}");
	}
}

static void Check(string name, ulong actual, ulong expected)
{
	if (actual != expected)
		throw new Exception($"{name}: ожидалось {expected}, получено {actual}");
	Console.WriteLine($"PASS: {name} = {actual}");
}
