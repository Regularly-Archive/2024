import os, dlib
import numpy as np
import cv2
from sklearn.cluster import DBSCAN
from sklearn.decomposition import PCA
from sklearn.neighbors import NearestNeighbors
import matplotlib.pyplot as plt
from sklearn.preprocessing import StandardScaler

detector = dlib.get_frontal_face_detector()
predictor = dlib.shape_predictor("./models/shape_predictor_68_face_landmarks.dat")
face_model = dlib.face_recognition_model_v1("./models/dlib_face_recognition_resnet_model_v1.dat")

def is_same_person(feature1, feature2, threshold=0.1):
    distance = np.linalg.norm(feature1 - feature2)
    return distance < threshold

# 重新分配簇集
def refine_clusters(features, labels):
    refined_labels = labels.copy()
    n_clusters = len(np.unique(labels))
    
    centers = []
    for i in range(n_clusters):
        cluster_features = features[labels == i]
        center = np.mean(cluster_features, axis=0)
        centers.append(center)
    
    for i, feature in enumerate(features):
        current_cluster = labels[i]
        center = centers[current_cluster]
        
        if not is_same_person(feature, center):
            for j, other_center in enumerate(centers):
                if j != current_cluster and is_same_person(feature, other_center):
                    refined_labels[i] = j
                    break
    
    return refined_labels

# 提取特征值
def extract_face_features(image_path):
    img = cv2.imread(image_path)
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

    faces = detector(gray)
    if len(faces) > 0:
        max_face = max(faces, key=lambda rect: rect.width() * rect.height())

        shape = predictor(gray, max_face)
        feature = face_model.compute_face_descriptor(img, shape)
        feature = np.array(feature)
        return feature

    return None

# 加载数据集
def load_dataset(dataset_path):
    X = []
    image_paths = []

    for image_name in os.listdir(dataset_path):
        image_path = os.path.join(dataset_path, image_name)
        features = extract_face_features(image_path)
        if features is not None:
            X.append(features)
            image_paths.append(image_path)

    return np.array(X), image_paths


def get_eps_by_k_distance_diagram(dataset_path):
    X, _ = load_dataset(dataset_path)
    
    # 定义 k 值，通常 k = min_samples - 1
    k = 5

    # 使用 NearestNeighbors 计算每个点的 k 个最近邻距离
    neigh = NearestNeighbors(n_neighbors=k)
    neigh.fit(X)
    distances, indices = neigh.kneighbors(X)

    # 只保留每个点到第 k 个最近邻的距离（即第 k 小的距离）
    k_distances = distances[:, k - 1]

    # 将 k-距离从小到大排序，准备绘制图表
    k_distances = np.sort(k_distances)[::-1]  # 降序排列

    # 绘制 K-Distance Graph
    plt.figure(figsize=(8, 4))
    plt.plot(range(1, len(k_distances) + 1), k_distances)
    plt.xlabel("Points sorted by distance")
    plt.ylabel(f"{k}-th Nearest Neighbor Distance")
    plt.title(f"{k}-Distance Graph")
    plt.grid(True)
    plt.show()

    
def face_clustering_pipeline(dataset_path):

    get_eps_by_k_distance_diagram(dataset_path)

    # 加载数据集
    features, valid_paths = load_dataset(dataset_path)

    scaler = StandardScaler()
    features = scaler.fit_transform(features)
    
    # 对向量进行降维
    pca = PCA(n_components=30)
    features = pca.fit_transform(features) 

    clustering = DBSCAN(eps=0.45, min_samples=6)
    labels = clustering.fit_predict(features)
    
    refined_labels = refine_clusters(np.array(features), labels)
    
    results = {}
    for path, label in zip(valid_paths, refined_labels):
        if label not in results:
            results[label] = []
        results[label].append(path)
    
    return results

# 使用示例
dataset_path = './collections'
clustered_faces = face_clustering_pipeline(dataset_path)

# 展示结果
for cluster_id, paths in clustered_faces.items():
    print(f"Cluster {cluster_id} has {len(paths)} images")