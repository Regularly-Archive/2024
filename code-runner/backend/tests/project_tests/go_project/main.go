package main

import (
	"encoding/json"
	"fmt"
	"math/rand"
	"time"

	"github.com/google/uuid"
	"github.com/sirupsen/logrus"
)

type CalculationResult struct {
	A         int    `json:"A"`
	B         int    `json:"B"`
	Sum       int    `json:"Sum"`
	Timestamp string `json:"Timestamp"`
	Language  string `json:"Language"`
	RequestID string `json:"RequestID"`
}

var log = logrus.New()

func init() {
	log.SetFormatter(&logrus.JSONFormatter{})
}

func calculate() *CalculationResult {
	// 生成随机数
	a := rand.Intn(100) + 1
	b := rand.Intn(100) + 1
	sum := a + b

	// 创建独特的请求ID
	requestID := uuid.New().String()

	log.WithFields(logrus.Fields{
		"a":         a,
		"b":         b,
		"sum":       sum,
		"requestID": requestID,
	}).Info("Calculation performed")

	return &CalculationResult{
		A:         a,
		B:         b,
		Sum:       sum,
		Timestamp: time.Now().Format(time.RFC3339),
		Language:  "Go Module",
		RequestID: requestID,
	}
}

func main() {
	log.Info("Go Module project starting...")

	// 执行计算
	result := calculate()

	// JSON 序列化
	jsonData, err := json.MarshalIndent(result, "", "  ")
	if err != nil {
		log.WithError(err).Error("Failed to marshal result")
		panic(err)
	}

	fmt.Println(string(jsonData))

	log.Info("Go Module project completed")
}