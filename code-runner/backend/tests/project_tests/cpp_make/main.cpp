#include <iostream>
#include <random>
#include <ctime>

int main() {
    std::mt19937 gen(static_cast<unsigned int>(std::time(nullptr)));
    std::uniform_int_distribution<> dist(1, 100);

    int a = dist(gen);
    int b = dist(gen);
    int sum = a + b;

    std::cout << "{\n";
    std::cout << "  \"A\": " << a << ",\n";
    std::cout << "  \"B\": " << b << ",\n";
    std::cout << "  \"Sum\": " << sum << ",\n";
    std::cout << "  \"Timestamp\": " << std::time(nullptr) << ",\n";
    std::cout << "  \"Language\": \"Makefile (C++)\"\n";
    std::cout << "}\n";

    return 0;
}
