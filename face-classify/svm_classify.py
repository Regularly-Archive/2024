import os
import cv2
import dlib
import numpy as np
from sklearn.model_selection import train_test_split
from sklearn.svm import SVC
from sklearn.metrics import classification_report, accuracy_score
from sklearn.preprocessing import StandardScaler
import joblib
from PIL import Image, ImageDraw, ImageFont
from kernels import select_best_kernel
from sklearn.decomposition import PCA


detector = dlib.get_frontal_face_detector()
predictor = dlib.shape_predictor("./models/shape_predictor_68_face_landmarks.dat")
face_model = dlib.face_recognition_model_v1("./models/dlib_face_recognition_resnet_model_v1.dat")
scaler = StandardScaler()
pca = PCA(n_components=30)

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

# 使用全部样本构建数据集
def load_dataset_from_features(dataset_path):
    X = []
    y = []

    for label in os.listdir(dataset_path):
        class_folder = os.path.join(dataset_path, label)
        if os.path.isdir(class_folder):
            for image_name in os.listdir(class_folder):
                image_path = os.path.join(class_folder, image_name)
                feature = extract_face_features(image_path)
                if feature is not None:
                    X.append(feature)
                    y.append(label)

    return np.array(X), np.array(y)

# 为每个样本单独构建数据集
def load_dataset_from_mean_features(dataset_path):
    train_dict = {}
    test_dict = {}

    for label in os.listdir(dataset_path):
        class_folder = os.path.join(dataset_path, label)
        if os.path.isdir(class_folder):
           image_paths = [os.path.join(class_folder, image_name) for image_name in os.listdir(class_folder)]
           train_images, test_images = train_test_split(image_paths, test_size=0.5, random_state=42)
           train_dict[label] = train_images
           test_dict[label] = test_images

    X_train = []
    y_train = []
    for label, image_paths in train_dict.items():
        features = [extract_face_features(image_path) for image_path in image_paths]
        features = list(filter(lambda x: x is not None, features))
        X_train.append(np.mean(features, axis=0))
        y_train.append(label)

    X_test = []
    y_test = []
    for label, image_paths in test_dict.items():
        features = [extract_face_features(image_path) for image_path in image_paths]
        features = list(filter(lambda x: x is not None, features))
        for feature in features:
            X_test.append(feature)
            y_test.append(label)

    return np.array(X_train), np.array(X_test), np.array(y_train), np.array(y_test)

def train(dataset_path, model_path):
    X, y = load_dataset_from_features(dataset_path)

    #X = scaler.fit_transform(X)

    X_pca = pca.fit_transform(X)

    X_train, X_test, y_train, y_test = train_test_split(X_pca, y, test_size=0.3, random_state=42)

    kernel = select_best_kernel(X_train, y_train)

    model = SVC(kernel='rbf', C=1, gamma='scale', probability=True)
    model.fit(X_train, y_train)

    y_pred = model.predict(X_test)

    print(classification_report(y_test, y_pred))
    print("Accuracy:", accuracy_score(y_test, y_pred))
    
    if os.path.exists(model_path):
        os.remove(model_path)
    joblib.dump(model, model_path)


def train_mean(dataset_path, model_path):
    X_train, X_test, y_train, y_test = load_dataset_from_mean_features(dataset_path)

    X_train = scaler.fit_transform(X_train)
    X_test = scaler.fit_transform(X_test)

    X_train = pca.fit_transform(X_train)
    X_test = pca.fit_transform(X_test)

    model = SVC(kernel='linear', C=1, probability=True)
    model.fit(X_train, y_train)

    y_pred = model.predict(X_test)

    print(classification_report(y_test, y_pred))
    print("Accuracy:", accuracy_score(y_test, y_pred))

    if os.path.exists(model_path):
        os.remove(model_path)
    joblib.dump(model, model_path)

def test(model, dataset_path):
    for image_name in os.listdir(dataset_path):
        image_path = os.path.join(dataset_path, image_name)
        features = extract_face_features(image_path)
        if features is None:
            continue
        
        #features = scaler.fit_transform([features])
        pca.fit_transform([features])
        pac_features = pca.transform(features.reshape(1, -1))

        person_name = model.predict(pac_features)[0]
        confidence = np.max(model.predict_proba(pac_features))
        label = f"{person_name}: {confidence:.2f}"

        img = cv2.imread(image_path)
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

        faces = detector(gray)

        if len(faces) > 0:
            face = max(faces, key=lambda rect: rect.width() * rect.height())
            x1, y1, x2, y2 = (face.left(), face.top(), face.right(), face.bottom())
            
            rgb_image = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
            image = Image.fromarray(rgb_image)
            draw = ImageDraw.Draw(image)

            draw.rectangle(((x1, y1), (x2, y2)), outline="green", width=2)
            
            font = ImageFont.truetype("arial.ttf", 18)
            text_bbox = draw.textbbox((0, 0), label, font=font)
            text_height = text_bbox[3] - text_bbox[1]
            text_x, text_y = x1, y1 - text_height - 5

            draw.text((text_x, text_y), label, fill="white", font=font)
            image.save(f"./output/model_test_results/{image_name}")
            print(f'已验证人脸 {image_path}')



if __name__ == "__main__":
    dataset_path = "./dataset"
    model_path = './output/pre_train_models/default.m'
    if not os.path.exists(model_path):
        #train_mean(dataset_path, model_path)
        train(dataset_path, model_path)
    
    model = joblib.load(model_path)
    test(model, './testcases')
    



    
