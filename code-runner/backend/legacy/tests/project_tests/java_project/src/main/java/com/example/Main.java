package com.example;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.util.Date;
import java.util.Random;

public class Main {
    public static void main(String[] args) {
        try {
            // 创建计算器服务对象
            CalculatorService calculator = new CalculatorService();

            // 执行计算
            Result result = calculator.calculate();

            // 使用 Jackson 序列化对象
            ObjectMapper mapper = new ObjectMapper();
            String json = mapper.writerWithDefaultPrettyPrinter()
                                .writeValueAsString(result);

            // 输出 JSON
            System.out.println(json);

        } catch (Exception e) {
            System.err.println("Error: " + e.getMessage());
            e.printStackTrace();
        }
    }
}

class CalculatorService {
    public Result calculate() {
        Random random = new Random();
        int a = random.nextInt(100) + 1;  // 1-100
        int b = random.nextInt(100) + 1;  // 1-100
        int sum = a + b;

        return new Result(a, b, sum, new Date(), "Java Maven");
    }
}

class Result {
    private final int a;
    private final int b;
    private final int sum;
    private final Date timestamp;
    private final String language;

    public Result(int a, int b, int sum, Date timestamp, String language) {
        this.a = a;
        this.b = b;
        this.sum = sum;
        this.timestamp = timestamp;
        this.language = language;
    }

    // Getters for JSON serialization
    public int getA() { return a; }
    public int getB() { return b; }
    public int getSum() { return sum; }
    public Date getTimestamp() { return timestamp; }
    public String getLanguage() { return language; }
}