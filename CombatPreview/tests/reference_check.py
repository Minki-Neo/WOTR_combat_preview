"""Independent mathematical oracle, NOT execution or compilation of the C# implementation."""
from fractions import Fraction
from itertools import product
from pathlib import Path
import json
import xml.etree.ElementTree as ET

checks=0
def check(actual, expected):
    global checks
    assert actual == expected, (actual,expected)
    checks+=1

def mean(values): return Fraction(sum(values),len(values))
sword=list(range(16,24))
check(mean(sword), Fraction(39,2))
crit=[a+b for a,b in product(sword,repeat=2)]
check(mean(crit),39)
check(Fraction(75,100)*mean(sword)+Fraction(75,1000)*(mean(crit)-mean(sword)),Fraction(1287,80))
check(mean([max(1,d-3) for d in range(1,7)]),Fraction(3,2))
check(mean([v+d for v,d in product(crit,range(1,7))]),Fraction(85,2))
for ab,expected in [(22,Fraction(3,4)),(17,Fraction(1,2)),(12,Fraction(1,4))]:
    check(Fraction(sum(d==20 or d>1 and d+ab>=28 for d in range(1,21)),20),expected)
check(Fraction(sum(max(a,b)>=6 for a,b in product(range(1,21),repeat=2)),400),Fraction(15,16))
root=Path(__file__).resolve().parents[1]
info=json.loads((root/'Info.json').read_text())
check(info['EntryMethod'],'CombatPreview.Main.Load')
check(info['AssemblyName'],'CombatPreview.dll')
ET.parse(root/'CombatPreview.csproj'); ET.parse(root/'tests/CombatPreview.Tests.csproj')
# Independently check reduction thresholds using exact rational arithmetic.
for sides in range(2,21):
    for resistance in range(26):
        enumerated=mean([max(0,d-resistance) for d in range(1,sides+1)])
        positive=max(0,sides-resistance)
        closed_form=Fraction(positive*(positive+1),2*sides)
        check(enumerated,closed_form)
check(mean([max(0,d-5) for d in range(1,7)]),Fraction(1,6))
check(mean([max(0,a+b-5) for a,b in product(range(1,7),repeat=2)]),Fraction(41,18))
check(mean([max(0,d-10) for d in range(16,24)])+Fraction(1,6),Fraction(29,3))
check(Fraction(675,1000)*Fraction(29,3)+Fraction(75,1000)*Fraction(175,6),Fraction(697,80))
print(f'PASS: {checks} independent math/manifest checks; 2 project XML files parsed')
print('C# implementation NOT executed by this script.')
