"""Syntax-only WOTR preprocessor + tree-sitter check. Does NOT check types or APIs.
Install: python -m pip install tree-sitter tree-sitter-c-sharp
"""
from pathlib import Path
import tree_sitter, tree_sitter_c_sharp
parser=tree_sitter.Parser(tree_sitter.Language(tree_sitter_c_sharp.language()))
def preprocess(text):
    stack=[]; active=True; output=[]
    for line in text.splitlines():
        stripped=line.strip()
        if stripped.startswith('#if '):
            expr=stripped[4:]
            assert expr in ('WOTR','!WOTR','KINGMAKER','!KINGMAKER'),expr
            cond=expr in ('WOTR','!KINGMAKER')
            stack.append((active,cond)); active=active and cond
        elif stripped=='#else':
            parent,cond=stack[-1]; active=parent and not cond
        elif stripped=='#endif': active=stack.pop()[0]
        else:
            output.append(line if active else '')
            continue
        output.append('')
    assert not stack
    return '\n'.join(output).encode()
root=Path(__file__).resolve().parents[1]
files=sorted(list((root/'src').rglob('*.cs')) + list((root/'tests').glob('*.cs')))
assert files, 'No source files selected'
for f in files:
    tree=parser.parse(preprocess(f.read_text()))
    assert not tree.root_node.has_error, f
print(f'PASS: WOTR syntax parse for {len(files)} C# files (not compilation)')
