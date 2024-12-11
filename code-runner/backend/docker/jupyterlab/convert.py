import os
import argparse
import nbformat
from nbconvert import HTMLExporter, NotebookExporter
from nbclient import NotebookClient

def notebook_to_html(notebook_path, output_path, kernel_name=None):
    with open(notebook_path, 'r', encoding='utf-8') as f:
        notebook_content = nbformat.read(f, as_version=4)
    
    # 执行笔记本
    client = NotebookClient(notebook_content, kernel_name=kernel_name)
    client.execute()

    # 导出结果
    html_exporter = HTMLExporter(template_name='basic')
    html_exporter.exclude_input = True

    (body, resources) = html_exporter.from_notebook_node(notebook_content)

    with open(output_path, 'w', encoding='utf-8') as f:
        f.write(body)

def notebook_to_notebook(notebook_path, output_path, kernel_name=None):
    with open(notebook_path, 'r', encoding='utf-8') as f:
        notebook_content = nbformat.read(f, as_version=4)
    
    # 执行笔记本
    client = NotebookClient(notebook_content, kernel_name=kernel_name)
    client.execute()
    
    # 隐藏输入
    for cell in notebook_content.cells:
        if cell.cell_type == 'code':
            cell.metadata['hide_input'] = True
            cell.source = ''

    with open(output_path, 'w', encoding='utf-8') as f:
        nbformat.write(notebook_content, f)

def main():
    parser = argparse.ArgumentParser(description='Convert Jupyter Notebook to HTML.')
    parser.add_argument('notebook_path', type=str, help='Path to the Jupyter Notebook file')
    parser.add_argument('output_path', type=str, help='Path to the output HTML file')
    parser.add_argument('--kernel', type=str, help='Kernel name to use')

    args = parser.parse_args()

    try:
        output_format = os.getenv('NBCONVERT_OUTPUT_FORMAT', 'html')
        if output_format == 'html':
            notebook_to_html(args.notebook_path, args.output_path, args.kernel)
        elif output_format == 'notebook':
            notebook_to_notebook(args.notebook_path, args.output_path, args.kernel)
        else:
            raise NotImplementedError(f"unsupported output format '{output_format}' for nbconvert")

        absolute_input_path = os.path.abspath(args.notebook_path)
        absolute_output_path = os.path.abspath(args.output_path)
        
        print(f"Notebook '{absolute_input_path}' 已成功转换为 HTML 并输出到 '{absolute_output_path}'")
    except Exception as e:
        absolute_input_path = os.path.abspath(args.notebook_path)
        print(f"Notebook '{absolute_input_path}' 转换失败: {e}")

if __name__ == "__main__":
    main()
