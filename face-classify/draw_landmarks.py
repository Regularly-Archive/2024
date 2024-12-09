import dlib
from PIL import Image, ImageDraw
import numpy as np

detector = dlib.get_frontal_face_detector()
predictor = dlib.shape_predictor('./models/shape_predictor_68_face_landmarks.dat')

image_path = './dataset/LiuYiFei/w640slw.jpg'
image = Image.open(image_path).convert("RGB")
draw = ImageDraw.Draw(image)

img = dlib.load_rgb_image(image_path)

dets = detector(img, 1)

if dets:
    max_face = max(dets, key=lambda rect: rect.width() * rect.height())
else:
    max_face = None

if max_face is not None:
    shape = predictor(img, max_face)
    
    for i in range(0, 68):
        x = shape.part(i).x
        y = shape.part(i).y
        draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill="red")
        draw.text((x + 5, y), str(i + 1), fill="red")

image.save('landmarks.jpg') 
image.show()