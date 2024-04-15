from time import time
import sys, logging
Embedding_Model_Cache_Folder = './models/embedding/'
Text_Generation_Model_Cache_Folder = './models/text-generation/'

def timer(logger):
    def wrapper(func):
        def decorate(*args,**kwargs):
            time_start = time()
            logger.debug(f'The method {func.__name__} execution starts...')
            result = func(*args, **kwargs)
            time_finish = time()
            time_spend = time_finish - time_start
            logger.debug(f'The method {func.__name__} execution finished in  {round(time_spend, 3)} seconds.')
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
