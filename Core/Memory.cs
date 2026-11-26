namespace misc64.Core;

/// <summary>Раздельные области памяти команд и данных</summary>
public class Memory
{
	public byte[] Code = new byte[65_536];
	public byte[] Data = new byte[65_536];

	public void CheckAddress(int address, int offset, string calledFrom)
	{
		if ((address < 0 || offset < 0 || offset > Data.Length || address > Data.Length - offset) && calledFrom == "Data")
			throw new CpuException(CpuError.MemoryAccess, $"Адрес слова 0x{address:X8} вне памяти 0..0x{Data.Length - 1:X8}.");
		else if ((address < 0 || offset < 0 || offset > Code.Length || address > Code.Length - offset) && calledFrom == "Code")
			throw new CpuException(CpuError.MemoryAccess, $"Адрес слова 0x{address:X8} вне памяти 0..0x{Code.Length - 1:X8}.");
		else if (calledFrom != "Data" && calledFrom != "Code")
			throw new ArgumentException($"CheckAddress должен вызываться только из Data или Code, а не из {calledFrom}.", nameof(calledFrom));
		else return;
	}

	public void DataWriteIn(int address, ulong value)
	{
		CheckAddress(address, 8, "Data");
		for (int i = 0; i < 8; i++)
		{
			byte currentByte = (byte)(value & 0xFF);
			Data[address + i] = currentByte;
			value = value >> 8;
		}
	}

	public ulong DataReadOut(int address)
	{
		CheckAddress(address, 8, "Data");
		ulong value = 0;
		for (int i = 0; i < 8; i++)
		{
			ulong currentByte = Data[address + i];
			value = value | (currentByte << (8 * i));
		}
		return value;
	}

	public void CodeWriteIn(int address, uint value)
	{
		CheckAddress(address, 4, "Code");
		for (int i = 0; i < 4; i++)
		{
			byte currentByte = (byte)(value & 0xFF);
			Code[address + i] = currentByte;
			value = value >> 8;
		}
	}

	public uint CodeReadOut(int address)
	{
		CheckAddress(address, 4, "Code");
		uint value = 0;
		for (int i = 0; i < 4; i++)
		{
			uint currentByte = (uint)(Code[address + i] & 0xFF);
			value = value | (currentByte << (8 * i));
		}
		return value;
	}
}
