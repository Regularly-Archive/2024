#include <stdio.h>
#include <stdlib.h>
#include <time.h>

int main() {
    srand((unsigned int)time(NULL));

    int a = rand() % 100 + 1;
    int b = rand() % 100 + 1;
    int sum = a + b;

    printf("{\n");
    printf("  \"A\": %d,\n", a);
    printf("  \"B\": %d,\n", b);
    printf("  \"Sum\": %d,\n", sum);
    printf("  \"Timestamp\": %ld,\n", time(NULL));
    printf("  \"Language\": \"C\"\n");
    printf("}\n");

    return 0;
}
