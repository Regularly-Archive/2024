#!/bin/bash
echo "=== Basic arithmetic ==="
echo "10 + 20 = $(expr 10 + 20)"
echo "Counter from 1 to 5:"
for i in {1..5}; do
    echo "Count: $i"
done