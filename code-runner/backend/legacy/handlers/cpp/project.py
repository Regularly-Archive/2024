from handlers.baseHandler import BaseHandler

class CPPProjectHandler(BaseHandler):
    
    def define_pipeline(self):
        pipeline = {}
        if 'Makefile' in self.ctx.project_info.files:
            pipeline['build'] = "make"
        else:
            if self.ctx.entry_point.endswith('.cpp'):
                pipeline['build'] = f"g++ {self.ctx.entry_point} -o main -std=c++17"
            else:
                pipeline['build'] = f"gcc {self.ctx.entry_point} -o main"

        pipeline['run'] = "./main"
        return pipeline