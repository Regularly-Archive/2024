package main

import (
	"encoding/json"
	"fmt"
	"math/rand"
	"time"
)

type Result struct {
	A         int    `json:"A"`
	B         int    `json:"B"`
	Sum       int    `json:"Sum"`
	Timestamp string `json:"Timestamp"`
	Language  string `json:"Language"`
}

func main() {
	// 初始化随机数种子
	rand.Seed(time.Now().UnixNano())

	// 生成随机数
	a := rand.Intn(100) + 1 // 1-100
	b := rand.Intn(100) + 1 // 1-100
	sum := a + b

	// 创建结果对象
	result := Result{
		A:         a,
		B:         b,
		Sum:       sum,
		Timestamp: time.Now().Format(time.RFC3339),
		Language:  "Go Single File",
	}

	// JSON 序列化
	jsonData, err := json.MarshalIndent(result, "", "  ")
	if err != nil {
		fmt.Printf("Error: %v\n", err)
		return
	}

	fmt.Println(string(jsonData))
}