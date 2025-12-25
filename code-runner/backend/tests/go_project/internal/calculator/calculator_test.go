package calculator

import (
	"encoding/json"
	"fmt"
	"testing"
)

func TestGenerateRandomCalculation(t *testing.T) {
	result := GenerateRandomCalculation()

	if result == nil {
		t.Fatal("GenerateRandomCalculation returned nil")
	}

	if result.A < 1 || result.A > 100 {
		t.Errorf("A is out of range: %d", result.A)
	}

	if result.B < 1 || result.B > 100 {
		t.Errorf("B is out of range: %d", result.B)
	}

	if result.Sum != result.A+result.B {
		t.Errorf("Sum is incorrect: got %d, want %d", result.Sum, result.A+result.B)
	}

	// 验证 JSON 序列化
	jsonStr, err := result.ToJSON()
	if err != nil {
		t.Fatalf("ToJSON failed: %v", err)
	}

	var parsed map[string]interface{}
	if err := json.Unmarshal([]byte(jsonStr), &parsed); err != nil {
		t.Fatalf("JSON unmarshaling failed: %v", err)
	}
}

func BenchmarkGenerateRandomCalculation(b *testing.B) {
	for i := 0; i < b.N; i++ {
		result := GenerateRandomCalculation()
		if result == nil {
			b.Fatal("result is nil")
		}
	}
}

func BenchmarkToJSON(b *testing.B) {
	result := GenerateRandomCalculation()
	b.ResetTimer()

	for i := 0; i < b.N; i++ {
		_, err := result.ToJSON()
		if err != nil {
			b.Fatal(err)
		}
	}
}

func BenchmarkGenerateRandomCalculationParallel(b *testing.B) {
	b.RunParallel(func(pb *testing.PB) {
		for pb.Next() {
			result := GenerateRandomCalculation()
			if result == nil {
				b.Fatal("result is nil")
			}
		}
	})
}

func BenchmarkToJSONParallel(b *testing.B) {
	result := GenerateRandomCalculation()
	b.ResetTimer()

	b.RunParallel(func(pb *testing.PB) {
		for pb.Next() {
			_, err := result.ToJSON()
			if err != nil {
				b.Fatal(err)
			}
		}
	})
}

func FuzzGenerateRandomCalculation(f *testing.F) {
	f.Fuzz(func(t *testing.T) {
		result := GenerateRandomCalculation()
		if result == nil {
			t.Fatal("result is nil")
		}

		if result.A < 1 || result.A > 100 {
			t.Errorf("A is out of range: %d", result.A)
		}

		if result.B < 1 || result.B > 100 {
			t.Errorf("B is out of range: %d", result.B)
		}
	})
}

func ExampleGenerateRandomCalculation() {
	result := GenerateRandomCalculation()
	fmt.Println("A:", result.A, "B:", result.B, "Sum:", result.Sum)
	// Output format like: A: XX B: XX Sum: XX
}