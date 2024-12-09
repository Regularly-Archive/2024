import os
import cv2
import dlib
import numpy as np
from sklearn.cluster import KMeans
import matplotlib.pyplot as plt
from sklearn.decomposition import PCA
import shutil
from sklearn.preprocessing import StandardScaler
from matplotlib.offsetbox import OffsetImage, AnnotationBbox

detector = dlib.get_frontal_face_detector()
predictor = dlib.shape_predictor("./models/shape_predictor_68_face_landmarks.dat")
face_model = dlib.face_recognition_model_v1("./models/dlib_face_recognition_resnet_model_v1.dat")

# 提取人脸特征
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

# 获取人脸范围
def extract_face_rect(image_path):
    img = cv2.imread(image_path)
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

    faces = detector(gray)

    if len(faces) > 0:
        face = max(faces, key=lambda rect: rect.width() * rect.height())
        left, top, right, bottom = (face.left(), face.top(), face.right(), face.bottom())
        
        clip = img[top:bottom, left:right]
        clip = cv2.cvtColor(clip, cv2.COLOR_BGR2RGB)
        return clip

    return None

# 加载数据集并提取特征
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

# 将图片移动到对应的聚类文件夹
def save_clustered_images(image_paths, labels, num_clusters, output_dir):
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)
    else:
        shutil.rmtree(output_dir)
        os.makedirs(output_dir)

    for cluster in range(num_clusters):
        cluster_dir = os.path.join(output_dir, f'Cluster_{cluster}')
        if not os.path.exists(cluster_dir):
            os.makedirs(cluster_dir)

        for i, label in enumerate(labels):
            if label == cluster:
                shutil.copy(image_paths[i], cluster_dir)


def get_k_by_elbow_method(X):
    sse = []
    k_range = range(1, 11) 

    for k in k_range:
        kmeans = KMeans(n_clusters=k)
        kmeans.fit(X)
        sse.append(kmeans.inertia_)

    plt.plot(k_range, sse, marker='o')
    plt.title('Elbow Method for Optimal k')
    plt.xlabel('Number of clusters (k)')
    plt.ylabel('SSE')
    plt.grid(True)
    plt.show()


def get_k_by_silhouette_score(X):
    from sklearn.metrics import silhouette_score

    silhouette_scores = []
    k_range = range(1, 11)

    for k in k_range[1:]:
        kmeans = KMeans(n_clusters=k)
        kmeans.fit(X)
        score = silhouette_score(X, kmeans.labels_)
        silhouette_scores.append(score)

    plt.plot(k_range[1:], silhouette_scores, marker='o')
    plt.title('Silhouette Score for Optimal k')
    plt.xlabel('Number of clusters (k)')
    plt.ylabel('Silhouette Score')
    plt.grid(True)
    plt.show()


def get_k_by_bouldin_score(X):
    from sklearn.metrics import davies_bouldin_score

    db_scores = []
    k_range = range(1, 11)

    for k in k_range[2:]:
        kmeans = KMeans(n_clusters=k)
        kmeans.fit(X)
        score = davies_bouldin_score(X, kmeans.labels_)
        db_scores.append(score)

    plt.plot(k_range[2:], db_scores, marker='o')
    plt.title('Davies-Bouldin Index for Optimal k')
    plt.xlabel('Number of clusters (k)')
    plt.ylabel('Davies-Bouldin Index')
    plt.grid(True)
    plt.show()


# 主程序
if __name__ == "__main__":
    dataset_path = "./collections"
    output_dir = "./output/clusering_results"
    X, image_paths = load_dataset(dataset_path)

    scaler = StandardScaler()
    X = scaler.fit_transform(X)

    get_k_by_bouldin_score(X)

    # 使用 K-means 进行聚类
    num_clusters = 6
    kmeans = KMeans(n_clusters=num_clusters, n_init=50, random_state=42, init='k-means++')
    kmeans.fit(X)

    # 获取聚类标签
    labels = kmeans.labels_

    # 将图片移动到对应的聚类文件夹
    save_clustered_images(image_paths, labels, num_clusters, output_dir)

    # 使用 PCA 降维到 2 维
    pca = PCA(n_components=2)
    X_reduced = pca.fit_transform(X)

    # 获取聚类质心
    centers = kmeans.cluster_centers_
    centers_reduced = pca.transform(centers)

    # 绘制每个聚类的散点
    plt.figure(figsize=(12, 10))

    # 对于每个聚类，绘制聚类的点和代表性图片
    for cluster in range(num_clusters):
        cluster_mask = labels == cluster
        cluster_points = X_reduced[cluster_mask]
        
        # 获取降维后的聚类中心坐标
        x_center, y_center = centers_reduced[cluster]  # 每个聚类的二维坐标

        # 绘制聚类中的数据点
        plt.scatter(cluster_points[:, 0], cluster_points[:, 1], label=f'Cluster {cluster}')

        # 选择每个聚类中最接近聚类中心的图像
        cluster_indices = np.where(cluster_mask)[0]
        distances = np.linalg.norm(X_reduced[cluster_mask] - centers_reduced[cluster], axis=1)
        closest_image_idx = cluster_indices[np.argmin(distances)]
        closest_image_path = image_paths[closest_image_idx]

        # 读取并处理该图片
        img = extract_face_rect(closest_image_path)
        h, w = img.shape[:2]

        # 设定目标图像大小
        target_size = 100
        aspect_ratio = w / h

        # 根据长宽比计算新宽高
        if aspect_ratio > 1:
            new_w = target_size
            new_h = int(target_size / aspect_ratio)
        else:
            new_h = target_size
            new_w = int(target_size * aspect_ratio)

        # 调整图片大小
        img_resized = cv2.resize(img, (new_w, new_h))

        # 在聚类中心绘制代表性图片
        imagebox = OffsetImage(img_resized, zoom=0.5)
        imagebox.image.axes = plt.gca()
        ab = AnnotationBbox(imagebox, (x_center, y_center),frameon=True,pad=0.5)
        plt.gca().add_artist(ab)

    # 绘制聚类中心
    plt.scatter(centers_reduced[:, 0], centers_reduced[:, 1], c='red', marker='X', s=200, label='CentroIds')

    # 标题、标签和图例
    plt.title('K-means Clustering Results (PCA Reduced)')
    plt.xlabel('PCA Dimension 1')
    plt.ylabel('PCA Dimension 2')
    plt.legend()
    plt.grid(True)
    plt.show()