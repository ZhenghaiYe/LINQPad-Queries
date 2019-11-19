<Query Kind="Program" />

void Main()
{
	/*
	>> 运算符将其左侧操作数向右移动右侧操作数定义的位数。
	右移位运算会放弃低阶位
	**/
	// 0b_1001
	uint x = 0b1001;
	Console.WriteLine($"Before: {Convert.ToString(x, toBase: 2),4}");

	uint y = x >> 2;
	Console.WriteLine($"After:  {Convert.ToString(y, toBase: 2),4}");
	// Output:
	// Before: 1001
	// After:    10

	/*
	如果左侧操作数的类型是 int 或 long，右移运算符将执行算术 移位：左侧操作数的最高有效位（符号位）的值将传播到高顺序空位位置。 
	也就是说，如果左侧操作数为非负，高顺序空位位置设置为零，如果为负，则将该位置设置为 1。
	**/
	int a = int.MinValue;
	Console.WriteLine($"Before: {Convert.ToString(a, toBase: 2)}");

	int b = a >> 3;
	Console.WriteLine($"After:  {Convert.ToString(b, toBase: 2)}");
	// Output:
	// Before: 10000000000000000000000000000000
	// After:  11110000000000000000000000000000

	int c = int.MaxValue;
	Console.WriteLine($"Before: {Convert.ToString(c, toBase: 2)}");

	int d = c >> 3;
	Console.WriteLine($"After:  {Convert.ToString(d, toBase: 2)}");
}

// Define other methods and classes here
