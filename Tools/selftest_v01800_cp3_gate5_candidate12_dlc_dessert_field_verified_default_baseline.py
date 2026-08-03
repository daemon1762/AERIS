#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read
suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 12 DLC Dessert Field-Verified Default Baseline')
foundation=read(ROOT/'GameData/AERISFlightControl/Airfields/Defaults/01_Stock_DLC_Foundation.cfg')
defaults=read(ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg')
parser=read(SOURCE/'Landing/AERISAirfieldConfigParser.cs')
reg=read(SOURCE/'Landing/AERISAirfieldRegistry.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
runner=read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py')
readme=read(ROOT/'README.md')

def kv(block):
    out={}
    for line in block.splitlines():
        if '=' in line:
            k,v=line.split('=',1); out[k.strip()]=v.strip()
    return out

dessert=foundation[foundation.find('id = DLC_DESSERT_AIRFIELD'):foundation.find('id = DLC_WOOMERANG')]
suite.check('validation = PrecisionValidated' in dessert and 'definitionVersion = 2' in dessert,
            'Dessert DLC definition promoted to field-verified baseline')
suite.check('id = DLC_DESSERT_MAIN' in dessert and 'displayName = RWY 36/18' in dessert,
            'Dessert represented as one reciprocal RWY 36/18 physical runway')
suite.check(dessert.count('certificationBasis = UserCalibrated')==2,
            'both configured DLC directions carry UserCalibrated authority')
suite.check(dessert.count('FIELD-VERIFIED BODY_FIXED_GEODETIC_ABSOLUTE DEFAULT 2026-08-02')==2,
            'both directions document exact field authority origin')
expected=[
 ('RWY_36','-6.5996178022817782','-144.04085510339206','822.713678324013','-6.4482987102736748','-144.0382863593405','822.6041791010648','0.9663908930630335'),
 ('RWY_18','-6.4482987102736748','-144.0382863593405','822.6041791010648','-6.5996178022817782','-144.04085510339206','822.713678324013','180.96639089306302'),
]
for rid,lat,lon,alt,olat,olon,oalt,hdg in expected:
    a=dessert.find('id = '+rid); b=dessert.find('\n            Direction',a+1)
    if b<0: b=len(dessert)
    block=dessert[a:b]
    tokens=('thresholdLat = '+lat,'thresholdLon = '+lon,'thresholdElevation = '+alt,
            'oppositeThresholdLat = '+olat,'oppositeThresholdLon = '+olon,
            'oppositeThresholdElevation = '+oalt,'heading = '+hdg)
    for token in tokens: suite.check(token in block,rid+' exact field value retained: '+token.split(' = ')[0])
suite.check('length = 1584.835184' in dessert,'Dessert A/B great-circle length recorded')
suite.check('id = DLC_WOOMERANG' in foundation and 'facilityType = LaunchPad' in foundation,
            'Woomerang remains excluded from fixed-wing runway operations')

blocks=re.findall(r'Calibration\s*\{(.*?)\n\}',defaults,re.S)
suite.check(len(blocks)==41,'shipped manual baseline now contains exactly 41 physical runways')
db=[kv(b) for b in blocks]; d=[x for x in db if x.get('name')=='Dessert Airfield']
suite.check(len(d)==1,'Dessert calibration appears exactly once in shipped defaults')
if d:
    x=d[0]
    exact={'providerSiteId':'Dessert Airfield','coordinateFrame':'BODY_FIXED_GEODETIC_ABSOLUTE','hasStart':'True','hasEnd':'True','reciprocalDirectionPair':'True','directionAHeadingDeg':'0.9663908930630335','directionBHeadingDeg':'180.96639089306302','startLat':'-6.5996178022817782','startLon':'-144.04085510339206','startAlt':'822.713678324013','endLat':'-6.4482987102736748','endLon':'-144.0382863593405','endAlt':'822.6041791010648'}
    for k,v in exact.items(): suite.check(x.get(k)==v,'Dessert default exact: '+k)

suite.check('"certificationBasis", AERISRunwayCertificationBasis.Unknown' in parser,
            'config parser supports explicit direction certification basis')
suite.check('direction.CertificationBasis = configuredBasis;' in parser and 'direction.CertificationBasisDetail = configuredBasisDetail;' in parser,
            'configured manual basis preserved on direction objects')
suite.check('configuredBasis == AERISRunwayCertificationBasis.UserCalibrated' in parser and 'AERISRunwayMeasurementMethod.M29UserCalibration' in parser,
            'configured manual directions receive M29 user calibration evidence')
suite.check('return record != null && record.Source == AERISAirfieldSource.Stock;' in reg,
            'automatic provider certification remains base-game Stock only')
suite.check('AERISRunwayCertificationBasis.UserCalibrated' in reg and 'MANUAL A/B CALIBRATION REQUIRED' in reg,
            'non-stock authority remains manual A/B only')
suite.check('VANILLA RUNWAYS' in window and 'dlcVanilla' in window and 'AERISAirfieldSource.Dlc' in window,
            'successor UI groups installed DLC under VANILLA while internal authority remains DLC/UserCalibrated')
suite.check('case AERISAirfieldSource.Dlc:' in reg and 'AERISExpansionStatus.MakingHistoryInstalled' in reg,
            'Making History install state still gates DLC presentation')
suite.check('IsDlcRunwayPresentationPlaceholder' in reg,'placeholder fallback remains for geometry-free DLC definitions')
suite.check('MARK A' in window and 'MARK B' in window and 'ClearUserRunwayCalibration' in window,
            'manual A/B correction/re-survey path remains available')
suite.check('A/B ABSOLUTE GEO' in read(SOURCE/'Landing/AERISRunwayWitnessLibrary.cs'),
            'exact absolute-geodetic endpoint display remains available')

frozen={
 'Landing/AERISAirfieldRegistry.cs':'c1e70635741b779f585d0dd3d7a486e0c5761588f14cee41a710ba4f69cf800e',
 'Terrain/AERISTerrainGpuTileRasterizer.cs':'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032',
 'Autopilot/AERISBankDirector.cs':'bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7',
 'Landing/AERISExpansionStatus.cs':'c69bd788e2dc5a6c03fa0741213672de0892bff837406d2dcf8480f7b09e2253'}
for rel,expected_hash in frozen.items():
    suite.check(hashlib.sha256((SOURCE/rel).read_bytes()).hexdigest()==expected_hash,
                'Candidate 11 protected boundary byte-identical: '+rel)
lineage='CANDIDATE 12 — DLC DESSERT FIELD-VERIFIED DEFAULT BASELINE'
suite.check(lineage in version and 'CANDIDATE 12 DLC DESSERT FIELD VERIFIED DEFAULT BASELINE' in build,
            'Candidate 12 baseline identity retained in successor build')
suite.check('Candidate 12 DLC Dessert Field-Verified Default Baseline' in avc,'Candidate 12 AVC identity')
suite.check('Candidate 12' in readme and 'RWY 36/18' in readme and 'UserCalibrated' in readme,
            'README documents Dessert field default and authority')
suite.check('selftest_v01800_cp3_gate5_candidate12_dlc_dessert_field_verified_default_baseline.py' in runner,
            'Gate 5 runner executes Candidate 12 regression')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE12_DLC_DESSERT_FIELD_VERIFIED_DEFAULT_BASELINE.txt').is_file(),'Candidate 12 acceptance present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE12_DLC_DESSERT_FIELD_VERIFIED_DEFAULT_BASELINE_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Candidate 12 test card present')
suite.check((ROOT/'Evidence/RUNTIME_EVIDENCE_AERISFlightControl51_DESSERT_FIELD_CALIBRATION.txt').is_file(),'Dessert field evidence present')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,'build_ubuntu.sh executable bit retained')
suite.finish()
