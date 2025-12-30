from handlers.baseHandler import BaseHandler

class JavaSingleFileHandler(BaseHandler):

    def define_pipeline(self):
        return {
            'run': f"export JAVA_TOOL_OPTIONS='-Dfile.encoding=UTF-8' && jbang {self.ctx.entry_point}"
        }
