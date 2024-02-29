from langchain.callbacks.streaming_stdout import StreamingStdOutCallbackHandler
from langchain_core.outputs import LLMResult
from typing import Any, Dict, List

STOP_FLAG = "[END]"
START_FLAG = "[START]"

class ChainStreamHandler(StreamingStdOutCallbackHandler):
    def __init__(self, queue):
        self.queue = queue
    
    def on_llm_start(self, serialized: Dict[str, Any], prompts: List[str], **kwargs: Any) -> None:
        self.queue.clear()
        self.queue.put(START_FLAG)
        self.finished = False
    
    def on_llm_new_token(self, token: str, **kwargs: Any) -> None:
        self.queue.put(token)

    def on_llm_end(self, response: LLMResult, **kwargs: Any) -> None:
        self.queue.put(STOP_FLAG)

    def on_llm_error(self, error: Exception, **kwargs):
        self.queue.put("%s: %s" % (type(error).__name__, str(error)))
        self.queue.put(STOP_FLAG)