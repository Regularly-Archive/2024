from transformers import AutoModelForCausalLM, AutoTokenizer
from models import CompletionRequest, ChatCompletionRequest
import torch

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

def chatCompletions(request: ChatCompletionRequest) -> str:
    model = AutoModelForCausalLM.from_pretrained(
        request.model,
        torch_dtype="auto",
        device_map="auto"
    )
    tokenizer = AutoTokenizer.from_pretrained(request.model)
    text = tokenizer.apply_chat_template(
        request.messages,
        tokenize=False,
        add_generation_prompt=True
    )
    model_inputs = tokenizer([text], return_tensors="pt").to(device)

    generated_ids = model.generate(
        model_inputs.input_ids,
        max_new_tokens=request.max_tokens
    )
    generated_ids = [
        output_ids[len(input_ids):] for input_ids, output_ids in zip(model_inputs.input_ids, generated_ids)
    ]

    response = tokenizer.batch_decode(generated_ids, skip_special_tokens=True)[0]
    return response

def textCompletions(request: CompletionRequest) -> str:
    model = AutoModelForCausalLM.from_pretrained(
        request.model,
        torch_dtype="auto",
        device_map="auto"
    )
    tokenizer = AutoTokenizer.from_pretrained(request.model)
    messages = []
    if isinstance(request.prompt, str):
        messages.append({"role": "user", "content": request.prompt})
    text = tokenizer.apply_chat_template(
        messages,
        tokenize=False,
        add_generation_prompt=True
    )
    model_inputs = tokenizer([text], return_tensors="pt").to(device)

    generated_ids = model.generate(
        model_inputs.input_ids,
        max_new_tokens=request.max_tokens
    )
    generated_ids = [
        output_ids[len(input_ids):] for input_ids, output_ids in zip(model_inputs.input_ids, generated_ids)
    ]

    response = tokenizer.batch_decode(generated_ids, skip_special_tokens=True)[0]
    return response