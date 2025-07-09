from transformers import BlipProcessor, BlipForConditionalGeneration, AutoTokenizer, AutoModelWithLMHead
from utils import timer, createLogger, LRUCache
from transformers import pipeline

logger = createLogger(__name__)

@timer(logger=logger)
def convert_image_to_text(image, model_name="Salesforce/blip-image-captioning-large"):
    processor, model = get_blip_model(model_name)

    inputs = processor(image, return_tensors="pt")
    output = model.generate(**inputs)
    generated_text = processor.decode(output[0], skip_special_tokens=True)
    return generated_text

def get_blip_model(blip_model_name="Salesforce/blip-image-captioning-base"):
    processor = BlipProcessor.from_pretrained(blip_model_name)
    model = BlipForConditionalGeneration.from_pretrained(blip_model_name)
    return processor, model

def translate(target_lang, text, model_name="google-t5/t5-small"):
    prompt = f"translate to {target_lang}: {text}"
    translator = pipeline(task="translation", model=model_name)
    return translator(prompt)[0]['translation_text']