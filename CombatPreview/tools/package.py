"""Build with user-supplied references, then package an explicit mod-only allowlist."""
import argparse
import json
from pathlib import Path
import subprocess
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--game-dir', type=Path)
parser.add_argument('--managed-dir', type=Path)
parser.add_argument('--umm-dir', type=Path)
args = parser.parse_args()
if not args.game_dir and not args.managed_dir:
    parser.error('Provide --game-dir or --managed-dir with your installed game references')
root = Path(__file__).resolve().parents[1]
managed = args.managed_dir or args.game_dir / 'Wrath_Data' / 'Managed'
umm = args.umm_dir or managed / 'UnityModManager'
for path in [managed/'Assembly-CSharp.dll', managed/'0Harmony.dll', umm/'UnityModManager.dll']:
    if not path.is_file():
        parser.error(f'Missing reference: {path}')
subprocess.run(['dotnet', 'build', 'CombatPreview.csproj', '-c', 'Release',
                f'-p:ManagedDir={managed.resolve()}', f'-p:UmmDir={umm.resolve()}'], cwd=root, check=True)
info = json.loads((root/'Info.json').read_text())
files = {
    'CombatPreview.dll': root/'bin/Release/net472/CombatPreview.dll',
    **{name: root/name for name in ['Info.json', 'LICENSE', 'THIRD_PARTY.md', 'README.ko.md']},
    'DIAGNOSTICS.ko.md': root/'docs/DIAGNOSTICS.ko.md',
}
output = root/'dist'/f"CombatPreview-{info['Version']}-beta-UMM.zip"
output.parent.mkdir(exist_ok=True)
# No shared staging directory: stale dependency/game DLLs cannot enter the ZIP.
with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as z:
    for name, source in files.items():
        z.write(source, 'CombatPreview/'+name)
with zipfile.ZipFile(output) as z:
    assert z.testzip() is None
    assert set(z.namelist()) == {'CombatPreview/'+name for name in files}
print(f'Created read-only beta UMM package (game UI execution unverified): {output}')
