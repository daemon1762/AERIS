#!/usr/bin/env python3
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,CheckSuite,sha256
suite=CheckSuite('CP3.75 Candidate6 package manifest verification')
manifest=ROOT/'MANIFEST_SHA256.txt'; suite.check(manifest.is_file(),'MANIFEST_SHA256.txt exists')
expected={}
if manifest.is_file():
 for n,line in enumerate(manifest.read_text(encoding='utf-8').splitlines(),1):
  if not line.strip(): continue
  parts=line.split('  ',1)
  if len(parts)!=2 or len(parts[0])!=64: suite.check(False,'package manifest line '+str(n)+' format',line); continue
  expected[parts[1].removeprefix('./')]=parts[0].lower()
actual=sorted(str(p.relative_to(ROOT)).replace('\\','/') for p in ROOT.rglob('*') if p.is_file() and p.name!='MANIFEST_SHA256.txt' and '.git' not in p.parts and '__pycache__' not in p.parts and p.suffix.lower()!='.pyc')
suite.equal(sorted(expected),actual,'package manifest file set matches package payload')
bad=[x for x in actual if expected.get(x)!=sha256(ROOT/x)]; suite.check(not bad,'all package manifest hashes match',', '.join(bad[:10])); suite.finish()
