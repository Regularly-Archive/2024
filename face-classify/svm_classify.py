import dlib
import cv2
import numpy as np
import os
from sklearn.svm import SVC
from sklearn.preprocessing import LabelEncoder
from sklearn.decomposition import PCA
from sklearn.model_selection import train_test_split, GridSearchCV
from sklearn.metrics import classification_report
import joblib
import matplotlib.pyplot as plt

# 初始化 dlib 的人脸检测器和特征提取器
face_detector = dlib.get_frontal_face_detector()
shape_predictor = dlib.shape_predictor('./models/shape_predictor_68_face_landmarks.dat')
face_recognition_model = dlib.face_recognition_model_v1('./models/dlib_face_recognition_resnet_model_v1.dat')

# 初始化模型路径
svm_model_path = './output/pre_train_models/svm_model.pkl'
pca_model_path = './output/pre_train_models/pca_model.pkl'
label_encoder_path = './output/pre_train_models/label_encoder.pkl'

def extract_face_features(image_path):
    """从图像中提取人脸特征"""
    # 读取图像
    img = cv2.imread(image_path)
    # 转换为RGB格式
    rgb_img = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)

    # 检测人脸
    faces = face_detector(rgb_img)

    if len(faces) == 0:
        return None

    # 获取第一个检测到的人脸
    face = faces[0]

    # 获取人脸关键点
    landmarks = shape_predictor(rgb_img, face)

    # 提取128维特征向量
    face_descriptor = face_recognition_model.compute_face_descriptor(rgb_img, landmarks)

    return np.array(face_descriptor)

def load_training_data(data_dir):
    """加载训练数据"""
    features = []
    labels = []

    # 遍历数据目录
    for person_name in os.listdir(data_dir):
        person_dir = os.path.join(data_dir, person_name)
        if not os.path.isdir(person_dir):
            continue

        # 处理每个人的图像
        for image_name in os.listdir(person_dir):
            image_path = os.path.join(person_dir, image_name)

            # 提取特征
            face_feature = extract_face_features(image_path)
            if face_feature is not None:
                features.append(face_feature)
                labels.append(person_name)

    return np.array(features), np.array(labels)

def train_svm_model_with_grid_search(features, labels, usePCA):
    """使用网格搜索训练SVM模型"""
    # 编码标签
    label_encoder = LabelEncoder()
    encoded_labels = label_encoder.fit_transform(labels)

    # PCA 降维
    pca_model = None
    if usePCA:
        pca_model = PCA(n_components=30)
        features = pca_model.fit_transform(features)

    # 分割训练集和测试集
    X_train, X_test, y_train, y_test = train_test_split(
        features, encoded_labels, test_size=0.2, random_state=42
    )

    # 定义参数网格
    param_grid = {
        'kernel': ['linear', 'rbf', 'poly'],
        'C': [0.1, 1, 10, 100],
        'gamma': ['scale', 'auto', 0.001, 0.01, 0.1],
        'degree': [2, 3, 4]  # 只用于 poly 核
    }

    # 初始化SVM
    svm = SVC(probability=True)

    # 使用网格搜索进行交叉验证
    grid_search = GridSearchCV(
        estimator=svm,
        param_grid=param_grid,
        cv=5,  # 5折交叉验证
        n_jobs=-1,  # 使用所有可用的CPU核心
        verbose=2,
        scoring='accuracy'
    )

    # 执行网格搜索
    print("开始网格搜索...")
    grid_search.fit(X_train, y_train)

    # 打印最佳参数
    print("最佳参数:", grid_search.best_params_)
    print("最佳交叉验证得分:", grid_search.best_score_)

    # 使用最佳模型在测试集上评估
    best_model = grid_search.best_estimator_
    y_pred = best_model.predict(X_test)

    # 打印详细的分类报告
    print("\n分类报告:")
    print(classification_report(y_test, y_pred,
                              target_names=label_encoder.classes_))

    # 保存模型和标签编码器
    joblib.dump(best_model,  svm_model_path)
    joblib.dump(label_encoder, label_encoder_path)
    
    if usePCA:
        joblib.dump(pca_model, pca_model_path)

    return best_model, label_encoder, pca_model

def predict_face(image_path, svm_model, label_encoder, pca_model):
    """预测人脸身份并可视化结果"""
    # 读取原始图像
    img = cv2.imread(image_path)
    if img is None:
        return "无法读取图像"
    
    # 转换为RGB格式(用于显示)
    rgb_img = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    
    # 检测人脸
    faces = face_detector(rgb_img)
    
    if len(faces) == 0:
        return "未检测到人脸"
    
    # 获取第一个检测到的人脸
    face = faces[0]
    
    # 获取人脸关键点
    landmarks = shape_predictor(rgb_img, face)
    
    # 提取特征向量
    face_descriptor = face_recognition_model.compute_face_descriptor(rgb_img, landmarks)
    face_feature = np.array(face_descriptor).reshape(1, -1)

    if pca_model != None:
        face_feature = pca_model.transform(face_feature )
    
    # 预测
    prediction = svm_model.predict(face_feature)
    probability = svm_model.predict_proba(face_feature).max()
    predicted_name = label_encoder.inverse_transform(prediction)[0]
    
    # 创建图像显示
    plt.figure(figsize=(12, 4))
    
    # 原始图像
    plt.subplot(1, 2, 1)
    plt.imshow(rgb_img)
    
    # 绘制人脸框
    x, y, w, h = face.left(), face.top(), face.width(), face.height()
    rect = plt.Rectangle((x, y), w, h, fill=False, color='green', linewidth=2)
    plt.gca().add_patch(rect)
    
    # 添加预测标签和置信度
    label = f"{predicted_name}\nConfidence: {probability:.2f}"
    plt.text(x, y-10, label, color='green', 
             bbox=dict(facecolor='white', alpha=0.8),
             fontsize=12)
    
    plt.title('Face Detection')
    plt.axis('off')
    
    # 绘制关键点图
    plt.subplot(1, 2, 2)
    plt.imshow(rgb_img)
    
    # 绘制68个关键点
    points = np.array([[p.x, p.y] for p in landmarks.parts()])
    plt.plot(points[:, 0], points[:, 1], 'ro', markersize=2)
    
    # 连接特定的关键点组以显示面部轮廓
    def draw_shape_lines_range(start, end, color='blue'):
        for i in range(start, end-1):
            plt.plot([points[i, 0], points[i+1, 0]], 
                    [points[i, 1], points[i+1, 1]], color, linewidth=1)
            
    # 绘制面部轮廓线
    draw_shape_lines_range(0, 17)    # 下巴轮廓
    draw_shape_lines_range(17, 22)   # 左眉
    draw_shape_lines_range(22, 27)   # 右眉
    draw_shape_lines_range(27, 31)   # 鼻梁
    draw_shape_lines_range(31, 36)   # 鼻子下部
    draw_shape_lines_range(36, 42)   # 左眼
    draw_shape_lines_range(42, 48)   # 右眼
    draw_shape_lines_range(48, 60)   # 外嘴唇
    draw_shape_lines_range(60, 68)   # 内嘴唇
    
    plt.title('Facial Landmarks')
    plt.axis('off')
    
    # 调整子图之间的间距
    plt.tight_layout()
    
    # 显示图像
    plt.show()
    
    return f"预测身份: {predicted_name} (置信度: {probability:.2f})"

def main():
    # 数据目录路径
    data_dir = "./dataset"

    # 加载训练数据
    print("加载训练数据...")
    features, labels = load_training_data(data_dir)

    # 训练模型（使用网格搜索）
    print("开始训练SVM模型...")
    svm_model, label_encoder, pca_model = train_svm_model_with_grid_search(features, labels, False)

    # 预测示例
    for item in os.listdir('./testcases'):
        test_image = os.path.join('./testcases', item)
        result = predict_face(test_image, svm_model, label_encoder, pca_model)
        print(result)

if __name__ == "__main__":
    main()