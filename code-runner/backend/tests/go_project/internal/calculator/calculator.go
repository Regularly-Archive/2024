// Package calculator 提供随机数计算功能
package calculator

import (
	"encoding/json"
	"math/rand"
	"time"
)

// Result 存储计算结果
type Result struct {
	A         int    `json:"A"`
	B         int    `json:"B"`
	Sum       int    `json:"Sum"`
	Timestamp string `json:"Timestamp"`
}

// GenerateRandomCalculation 生成随机数计算
func GenerateRandomCalculation() *Result {
	rand.Seed(time.Now().UnixNano())
	a := rand.Intn(100) + 1
	b := rand.Intn(100) + 1

	return &Result{
		A:         a,
		B:         b,
		Sum:       a + b,
		Timestamp: time.Now().Format(time.RFC3339),
	}
}

// ToJSON 将结果转换为 JSON 格式
func (r *Result) ToJSON() (string, error) {
	data, err := json.MarshalIndent(r, "", "  ")
	if err != nil {
		return "", err
	}
	return string(data), nil
}