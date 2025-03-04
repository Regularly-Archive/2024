from transformers import AutoModelForCausalLM, AutoTokenizer
from models import CompletionRequest, ChatCompletionRequest, Usage
from utils import Text_Generation_Model_Cache_Folder as model_cache_folder
from utils import timer, createLogger, LRUCache
import os, gc, torch, asyncio
from concurrent.futures import ThreadPoolExecutor

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
logger = createLogger(__name__)

max_workers = int(os.getenv('MAX-WORKERS', '10'))
executor = ThreadPoolExecutor(max_workers)

cached_models = LRUCache(3)
cached_tokenizers = LRUCache(3)

@timer(logger=logger)
def get_chat_completion(request: ChatCompletionRequest) -> tuple[str, Usage]:
    cache_dir = os.path.join(model_cache_folder, request.model)

    (model, tokenizer) = get_cached_model(request.model, cache_dir)

    text = tokenizer.apply_chat_template(request.messages, tokenize=False, add_generation_prompt=True)
    model_inputs = tokenizer([text], return_tensors="pt").to(device)
    
    prompt_tokens = len(model_inputs.input_ids[0])

    generated_ids = model.generate(model_inputs.input_ids, max_new_tokens=request.max_tokens)
    generated_ids = [output_ids[len(input_ids):] for input_ids, output_ids in zip(model_inputs.input_ids, generated_ids)]

    completion_tokens = len(generated_ids[0])
    
    response = tokenizer.batch_decode(generated_ids, skip_special_tokens=True)[0]

    usage = Usage(
        prompt_tokens=prompt_tokens,
        completion_tokens=completion_tokens,
        total_tokens=prompt_tokens + completion_tokens
    )
    
    return response, usage

@timer(logger=logger)
def get_text_completion(request: CompletionRequest) -> tuple[str, Usage]:
    cache_dir = os.path.join(model_cache_folder, request.model)

    (model, tokenizer) = get_cached_model(request.model, cache_dir)
    
    messages = [{"role": "user", "content": request.prompt}] if isinstance(request.prompt, str) else request.prompt

    text = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
    model_inputs = tokenizer([text], return_tensors="pt").to(device)
    
    prompt_tokens = len(model_inputs.input_ids[0])

    generated_ids = model.generate(model_inputs.input_ids, max_new_tokens=request.max_tokens)
    generated_ids = [output_ids[len(input_ids):] for input_ids, output_ids in zip(model_inputs.input_ids, generated_ids)]

    completion_tokens = len(generated_ids[0])
    
    response = tokenizer.batch_decode(generated_ids, skip_special_tokens=True)[0]
    
    usage = Usage(
        prompt_tokens=prompt_tokens,
        completion_tokens=completion_tokens,
        total_tokens=prompt_tokens + completion_tokens
    )
    
    return response, usage

def get_cached_model(model_name: str, cache_dir: str):
    if cached_models.hasKey(model_name):
        return (cached_models.get(model_name), cached_tokenizers.get(model_name))
    else:
        model = AutoModelForCausalLM.from_pretrained(model_name, torch_dtype="auto", device_map="auto", cache_dir=cache_dir)
        tokenizer = AutoTokenizer.from_pretrained(model_name)
        cached_models.put(model_name, model)
        cached_tokenizers.put(model_name, tokenizer)
        return (model, tokenizer)

async def get_chat_completion_async(request: ChatCompletionRequest) -> tuple[str, Usage]:
    loop = asyncio.get_event_loop()
    completion, usage = await loop.run_in_executor(executor, get_chat_completion, request)
    return completion, usage

async def get_text_completion_async(request: CompletionRequest) -> tuple[str, Usage]:
    loop = asyncio.get_event_loop()
    completion, usage = await loop.run_in_executor(executor, get_text_completion, request)
    return completion, usage

def release_completion_models():
    gc.collect()