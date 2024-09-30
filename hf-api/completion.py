from transformers import AutoModelForCausalLM, AutoTokenizer
from models import CompletionRequest, ChatCompletionRequest
from utils import Text_Generation_Model_Cache_Folder as model_cache_folder
from utils import timer, createLogger
import os, torch, asyncio
from concurrent.futures import ThreadPoolExecutor

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
logger = createLogger(__name__)
executor = ThreadPoolExecutor()

@timer(logger=logger)
def get_chat_completion(request: ChatCompletionRequest) -> str:
    cache_dir = os.path.join(model_cache_folder, request.model)
    model = AutoModelForCausalLM.from_pretrained(request.model, torch_dtype="auto", device_map="auto", cache_dir=cache_dir)
    tokenizer = AutoTokenizer.from_pretrained(request.model)
    text = tokenizer.apply_chat_template(request.messages, tokenize=False, add_generation_prompt=True)
    model_inputs = tokenizer([text], return_tensors="pt").to(device)

    generated_ids = model.generate(model_inputs.input_ids, max_new_tokens=request.max_tokens)
    generated_ids = [output_ids[len(input_ids):] for input_ids, output_ids in zip(model_inputs.input_ids, generated_ids)]

    response = tokenizer.batch_decode(generated_ids, skip_special_tokens=True)[0]
    return response

@timer(logger=logger)
def get_text_completion(request: CompletionRequest) -> str:
    cache_dir = os.path.join(model_cache_folder, request.model)
    model = AutoModelForCausalLM.from_pretrained(request.model, torch_dtype="auto", device_map="auto", cache_dir=cache_dir, trust_remote_code=True)
    tokenizer = AutoTokenizer.from_pretrained(request.model)
    messages = [{"role": "user", "content": request.prompt}] if isinstance(request.prompt, str) else request.prompt

    text = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
    model_inputs = tokenizer([text], return_tensors="pt").to(device)

    generated_ids = model.generate(model_inputs.input_ids, max_new_tokens=request.max_tokens)
    generated_ids = [output_ids[len(input_ids):] for input_ids, output_ids in zip(model_inputs.input_ids, generated_ids)]

    response = tokenizer.batch_decode(generated_ids, skip_special_tokens=True)[0]
    return response

async def get_chat_completion_async(request: ChatCompletionRequest) -> str:
    loop = asyncio.get_event_loop()
    completion = await loop.run_in_executor(executor, get_chat_completion, request)
    return completion

async def get_text_completion_async(request: CompletionRequest) -> str:
    loop = asyncio.get_event_loop()
    completion = await loop.run_in_executor(executor, get_text_completion, request)
    return completion