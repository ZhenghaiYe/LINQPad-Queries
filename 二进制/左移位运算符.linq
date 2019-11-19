<Query Kind="Program" />

void Main()
{
	/*
	<< 运算符将其左侧操作数向左移动右侧操作数定义的位数。
	左移运算会放弃超出结果类型范围的高阶位，并将低阶空位位置设置为零
	**/
	// 0b_1100_1001_0000_0000_0000_0000_0001_0001
	uint x = 0b11001001000000000000000000010001;
	Console.WriteLine($"Before: {Convert.ToString(x, toBase: 2)}");

	uint y = x << 4;
	Console.WriteLine($"After:  {Convert.ToString(y, toBase: 2)}");
	// Output:
	// Before: 1100 1001 0000 0000 0000 0000 0001 0001
	// After:  1001 0000 0000 0000 0000 0001 0001 0000

	/*
	由于移位运算符仅针对 int、uint、long 和 ulong 类型定义，因此运算的结果始终包含至少 32 位。 
	如果左侧操作数是其他整数类型（sbyte、byte、short、ushort 或 char），则其值将转换为 int 类型
	**/
	// 0b_1111_0001
	byte a = 0b1111_0001;

	var b = a << 8;
	Console.WriteLine(b.GetType());
	Console.WriteLine($"Shifted byte: {Convert.ToString(b, toBase: 2)}");
	// Output:
	// System.Int32
	// Shifted byte: 1111000100000000
}
