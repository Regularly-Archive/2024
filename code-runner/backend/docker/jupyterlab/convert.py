import os
import argparse
import nbformat
from nbconvert import HTMLExporter, NotebookExporter
from nbclient import NotebookClient

def execute_notebook(notebook_path, kernel_name=None):
    with open(notebook_path, 'r', encoding='utf-8') as f:
        nb = nbformat.read(f, as_version=4)
        client = NotebookClient(nb, kernel_name=kernel_name)
        client.execute()
        return nb

def notebook_to_html(notebook_path, output_path, kernel_name=None, template_name=None):
    # 执行笔记本
    nb = execute_notebook(notebook_path, kernel_name)

    # 导出结果
    html_exporter = HTMLExporter(template_name=template_name)
    html_exporter.exclude_input = True

    (body, resources) = html_exporter.from_notebook_node(nb)

    with open(output_path, 'w', encoding='utf-8') as f:
        f.write(body)

def notebook_to_notebook(notebook_path, output_path, kernel_name=None):
    # 执行笔记本
    nb = execute_notebook(notebook_path, kernel_name)
    
    # 隐藏输入
    for cell in nb.cells:
        if cell.cell_type == 'code':
            cell.metadata['hide_input'] = True

    with open(output_path, 'w', encoding='utf-8') as f:
        nbformat.write(nb, f)

def main():
    parser = argparse.ArgumentParser(description='Convert Jupyter Notebook to HTML.')
    parser.add_argument('notebook_path', type=str, help='Path to the Jupyter Notebook file')
    parser.add_argument('output_path', type=str, help='Path to the output HTML file')
    parser.add_argument('--template', type=str, default='basic', help='nbconvert template to use')
    parser.add_argument('--kernel', type=str, help='Kernel name to use')

    args = parser.parse_args()

    try:
        notebook_path = os.path.abspath(args.notebook_path)
        output_path = os.path.abspath(args.output_path)

        output_format = os.getenv('NBCONVERT_OUTPUT_FORMAT', 'html')
        if output_format == 'html':
            notebook_to_html(notebook_path, output_path, args.kernel, args.template)
        elif output_format == 'notebook':
            notebook_to_notebook(notebook_path, output_path, args.kernel)
        else:
            raise NotImplementedError(f"unsupported output format '{output_format}' for nbconvert")
        

        print(f"The notebook '{notebook_path}' execution completed, saved output to'{output_path}'")

    except Exception as e:
        print(f"The notebook '{notebook_path}' execution failed: {e}")

if __name__ == "__main__":
    main()
