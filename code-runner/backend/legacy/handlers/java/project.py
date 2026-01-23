from handlers.baseHandler import BaseHandler

class JavaProjectHandler(BaseHandler):

    def define_pipeline(self):
        return {
            'build': "mvn compile",
            'run': "mvn exec:java -Dexec.mainClass={main_class}"
        }