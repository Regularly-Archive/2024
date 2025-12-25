import { randomInt } from 'crypto';

interface CalculationResult {
  A: number;
  B: number;
  Sum: number;
  Timestamp: string;
  Language: string;
}

function main(): void {
  // 生成随机数
  const a = randomInt(1, 101);  // 1-100
  const b = randomInt(1, 101);  // 1-100
  const sum = a + b;

  // 创建结果对象
  const result: CalculationResult = {
    A: a,
    B: b,
    Sum: sum,
    Timestamp: new Date().toISOString(),
    Language: "TypeScript"
  };

  // 输出 JSON
  console.log(JSON.stringify(result, null, 2));
}

main();