import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

# Create multiple plots
fig, axes = plt.subplots(2, 2, figsize=(12, 10))

# Plot 1: Sine wave
x = np.linspace(0, 2 * np.pi, 100)
y = np.sin(x)
axes[0, 0].plot(x, y, 'b-', linewidth=2)
axes[0, 0].set_title('Sine Wave')
axes[0, 0].grid(True)

# Plot 2: Bar chart
categories = ['A', 'B', 'C', 'D', 'E']
values = [23, 45, 56, 78, 32]
axes[0, 1].bar(categories, values, color='green', alpha=0.7)
axes[0, 1].set_title('Bar Chart')
axes[0, 1].grid(True, axis='y')

# Plot 3: Scatter plot
x_scatter = np.random.rand(50)
y_scatter = np.random.rand(50)
axes[1, 0].scatter(x_scatter, y_scatter, c='red', alpha=0.5)
axes[1, 0].set_title('Scatter Plot')
axes[1, 0].grid(True)

# Plot 4: Pie chart
sizes = [15, 30, 45, 10]
labels = ['Frogs', 'Hogs', 'Dogs', 'Logs']
axes[1, 1].pie(sizes, labels=labels, autopct='%1.1f%%', startangle=90)
axes[1, 1].set_title('Pie Chart')

plt.suptitle('Multi-Plot Analysis', fontsize=16, fontweight='bold')
plt.tight_layout()

# Save figure
output_path = "analysis_plots.png"
plt.savefig(output_path, dpi=150, bbox_inches='tight')
plt.close()

print(f"Plot saved: {output_path}")
print("Analysis complete!")
