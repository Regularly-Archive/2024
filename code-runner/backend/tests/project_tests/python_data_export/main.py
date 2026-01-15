import csv
import json

# Generate sample data
data = [
    {"id": i, "name": f"User_{i}", "email": f"user{i}@example.com", "score": 100 - i}
    for i in range(1, 51)
]

# Write to CSV
csv_path = "users_export.csv"
with open(csv_path, 'w', newline='', encoding='utf-8') as f:
    writer = csv.DictWriter(f, fieldnames=["id", "name", "email", "score"])
    writer.writeheader()
    writer.writerows(data)

print(f"CSV exported: {csv_path}")
print(f"Total records: {len(data)}")

# Write summary to JSON
summary = {
    "total_users": len(data),
    "columns": ["id", "name", "email", "score"],
    "sample": data[:3],
    "generated_by": "python_data_export"
}

json_path = "export_summary.json"
with open(json_path, 'w', encoding='utf-8') as f:
    json.dump(summary, f, indent=2)

print(f"JSON summary: {json_path}")
print("Export complete!")
