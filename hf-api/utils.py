from time import time
import sys, logging
from collections import OrderedDict

Embedding_Model_Cache_Folder = './.cached_models/embedding/'
Text_Generation_Model_Cache_Folder = './.cached_models/text-generation/'

def timer(logger):
    def wrapper(func):
        def decorate(*args,**kwargs):
            time_start = time()
            logger.debug(f'The execution of method {func.__name__} starts...')
            result = func(*args, **kwargs)
            time_finish = time()
            time_spend = time_finish - time_start
            logger.debug(f'The execution of method {func.__name__} finished in  {round(time_spend, 3)} seconds.')
            return result
        return decorate
    return wrapper

def createLogger(name):
    logger = logging.getLogger(name)
    logger.setLevel(logging.DEBUG)
    stream_handler = logging.StreamHandler(sys.stdout)
    log_formatter = logging.Formatter("%(asctime)s [%(processName)s: %(process)d] [%(threadName)s: %(thread)d] [%(levelname)s] %(name)s: %(message)s")
    stream_handler.setFormatter(log_formatter)
    logger.addHandler(stream_handler)
    return logger


class LRUCache:
    def __init__(self, capacity: int):
        self.cache = OrderedDict()
        self.capacity = capacity

    def get(self, key: str) -> object:
        if key not in self.cache:
            return None
        else:
            self.cache.move_to_end(key)
            return self.cache[key]

    def put(self, key: str, value: object) -> None:
        if key in self.cache:
            del self.cache[key]
        elif len(self.cache) >= self.capacity:
            self.cache.popitem(last=False)
        self.cache[key] = value

    def hasKey(self, key: str):
        return key in self.cache
