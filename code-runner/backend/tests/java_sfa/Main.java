///usr/bin/env jbang "$0" "$@" ; exit $?

//DEPS com.google.code.gson:gson:2.10.1
//DEPS org.slf4j:slf4j-simple:2.0.9

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import java.util.Date;
import java.util.Random;

public class Main {
    private static final Gson gson = new GsonBuilder().setPrettyPrinting().create();

    public static void main(String[] args) {
        try {
            // 生成随机数
            Random random = new Random();
            int a = random.nextInt(100) + 1;  // 1-100
            int b = random.nextInt(100) + 1;  // 1-100
            int sum = a + b;

            // 创建结果对象
            CalculationResult result = new CalculationResult(
                a, b, sum, new Date(), "Java Single File"
            );

            // 使用 Gson 序列化为 JSON
            String json = gson.toJson(result);

            // 输出结果
            System.out.println(json);

        } catch (Exception e) {
            System.err.println("Error occurred: " + e.getMessage());
            e.printStackTrace();
        }
    }

    static class CalculationResult {
        private final int a;
        private final int b;
        private final int sum;
        private final Date timestamp;
        private final String language;

        public CalculationResult(int a, int b, int sum, Date timestamp, String language) {
            this.a = a;
            this.b = b;
            this.sum = sum;
            this.timestamp = timestamp;
            this.language = language;
        }
    }
}