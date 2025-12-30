from handlers.baseHandler import BaseHandler

class GoFileHandler(BaseHandler):

    def define_pipeline(self):
        return {
            'run': f"go run {self.ctx.entry_point}"
        }
    