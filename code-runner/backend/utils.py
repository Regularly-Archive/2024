import nbformat
import re

def code_to_ipynb(code_string, notebook_name='output_notebook.ipynb'):
    nb = nbformat.v4.new_notebook()

    code_cell = nbformat.v4.new_code_cell(code_string)

    nb['cells'].append(code_cell)

    with open(notebook_name, 'w', encoding='utf-8') as f:
        nbformat.write(nb, f)

def code_to_file(code_string, file_path):
    with open(file_path, 'w') as f:
        f.write(code_string)


def remove_ansi_sequences(input_string):
    ansi_escape = re.compile(r'\x1b\[([0-?]*[ -/]*[@-~])')
    return ansi_escape.sub('', input_string).replace('\x1b=','')
    


def clean_ansi_codes(log):
    ansi_escape = re.compile(r'\x1b\[[0-9;]*[mK]')
    return ansi_escape.sub('', log)