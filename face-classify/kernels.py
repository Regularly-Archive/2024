from sklearn.svm import SVC
from sklearn.model_selection import cross_val_score
import numpy as np

def compare_kernels(X, y):
    # 定义不同核函数的SVM
    kernels = {
        'linear': SVC(kernel='linear'),
        'rbf': SVC(kernel='rbf'),
        'poly2': SVC(kernel='poly', degree=2),
        'poly3': SVC(kernel='poly', degree=3),
        'sigmoid': SVC(kernel='sigmoid')
    }
    
    # 比较结果
    results = {}
    for name, model in kernels.items():
        # 5折交叉验证
        scores = cross_val_score(model, X, y, cv=6)
        results[name] = {
            'mean_score': scores.mean(),
            'std_score': scores.std()
        }
    
    return results

# 使用示例
def select_best_kernel(X, y):
    # 比较不同核函数
    results = compare_kernels(X, y)
    
    # 输出结果
    print("各核函数表现：")
    for kernel, scores in results.items():
        print(f"{kernel:8s}: {scores['mean_score']:.3f} (+/- {scores['std_score']*2:.3f})")
    
    # 选择最佳核函数
    best_kernel = max(results.items(), key=lambda x: x[1]['mean_score'])[0]
    
    # 网格搜索优化最佳核函数的参数
    from sklearn.model_selection import GridSearchCV
    
    if best_kernel == 'rbf':
        param_grid = {
            'C': [0.01, 0.1, 1, 10, 100, 1000],
            'gamma': ['scale', 'auto', 0.1, 1, 10]
        }
    elif best_kernel == 'poly':
        param_grid = {
            'C': [0.01, 0.1, 1, 10, 100, 1000],
            'degree': [2, 3, 4],
            'coef0': [0, 1, 2]
        }
    else:  # linear
        param_grid = {
            'C': [0.1, 1, 10, 100]
        }
    
    svm = SVC(kernel=best_kernel)
    grid_search = GridSearchCV(svm, param_grid, cv=5)
    grid_search.fit(X, y)
    
    print(f"\n最佳核函数: {best_kernel}")
    print(f"最佳参数: {grid_search.best_params_}")
    print(f"最佳得分: {grid_search.best_score_:.3f}")
    
    return grid_search.best_estimator_
