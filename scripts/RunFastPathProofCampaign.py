"""Evidence-only ISO to CHD proof runner; merge reports with AccumulateFastPathProof.ps1."""

import argparse
import datetime
import hashlib
import json
import os
import pathlib
import re
import sqlite3
import subprocess
import tempfile
import zlib

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--corpus',type=pathlib.Path,required=True)
parser.add_argument('--report',type=pathlib.Path,required=True)
parser.add_argument('--redump-db',type=pathlib.Path,required=True)
parser.add_argument('--max-files',type=int,default=500)
args=parser.parse_args()
CORPUS=args.corpus.resolve(strict=True)
REPORT=args.report.resolve()
CHDMAN=(pathlib.Path(__file__).resolve().parent.parent/'Tools'/'chdman.exe').resolve(strict=True)
DB=args.redump_db.resolve(strict=True)
if not CORPUS.is_dir() or not CHDMAN.is_file() or not 1 <= args.max_files <= 100_000:
    parser.error('Invalid corpus, bundled chdman, or max-files value')
if REPORT.exists():
    parser.error('Report already exists; use a new path for each run')
if os.path.normcase(os.path.commonpath((CORPUS,REPORT)))==os.path.normcase(str(CORPUS)):
    parser.error('Report must be outside the corpus')
sources=sorted(CORPUS.rglob('*.iso'))
if len(sources)>args.max_files:
    parser.error(f'Corpus has {len(sources)} ISOs, exceeding --max-files {args.max_files}')
WORK=pathlib.Path(tempfile.mkdtemp(prefix='fastpath-proof-',dir=tempfile.gettempdir()))
if os.path.normcase(os.path.commonpath((CORPUS,WORK)))==os.path.normcase(str(CORPUS)):
    WORK.rmdir()
    parser.error('Temporary CHD directory is inside the corpus')
def run(*args):
    p=subprocess.run([str(CHDMAN),*map(str,args)],capture_output=True,text=True,errors='replace')
    return {'exitCode':p.returncode,'stdout':p.stdout.strip(),'stderr':p.stderr.strip()}
def iso_root(path):
    with path.open('rb') as f:
        f.seek(16*2048); p=f.read(2048)
        if len(p)!=2048 or p[:7]!=b'\x01CD001\x01': return None,'Missing ISO9660 primary volume descriptor'
        r=p[156:190]
        l=int.from_bytes(r[2:6],'little'); s=int.from_bytes(r[10:14],'little')
        if l<0 or s<=0 or s>1024*1024 or l*2048+s>path.stat().st_size: return None,'Invalid root directory extent'
        f.seek(l*2048); data=f.read(s); entries={}
        at=0
        while at<len(data):
            n=data[at]
            if n==0: at=((at//2048)+1)*2048; continue
            rec=data[at:at+n]
            if len(rec)<34: return None,'Truncated directory entry'
            name=rec[33:33+rec[32]].decode('latin1','replace').upper().split(';')[0]
            if name not in ('\x00','\x01'):
                entries[name]={'directory':bool(rec[25]&2),'extent':int.from_bytes(rec[2:6],'little'),'size':int.from_bytes(rec[10:14],'little')}
            at+=n
        def small_text(name):
            e=entries.get(name)
            if not e or e['directory'] or e['size']>4096 or e['extent']*2048+e['size']>path.stat().st_size: return None
            f.seek(e['extent']*2048); return f.read(e['size']).decode('latin1','replace')
        return {'pvd':'ISO9660','rootEntries':sorted(entries),'systemCnf':small_text('SYSTEM.CNF'),'umdData':small_text('UMD_DATA.BIN'),'hasPspGame':entries.get('PSP_GAME',{}).get('directory',False)},None
def classify(path):
    root,error=iso_root(path)
    if error: return 'Unknown',{'error':error}
    psp=root['hasPspGame'] and root['umdData'] and re.match(r'^[A-Z]{4}-\d{5}\|',root['umdData'])
    ps2=root['systemCnf'] and re.search(r'(?im)^\s*BOOT2\s*=\s*cdrom0:\\[A-Z]{4}_\d{3}\.\d{2};1',root['systemCnf'])
    evidence={'pvd':root['pvd'],'rootEntries':root['rootEntries'],'systemCnf':root['systemCnf'],'umdData':root['umdData']}
    if psp and not ps2: return 'PSP',evidence
    if ps2 and not psp:
        evidence['failure']='PS2 boot identity established; CD versus DVD requires authoritative media evidence'
        return 'PS2-unknown-media',evidence
    return 'Unknown',dict(evidence,failure='Conflicting or missing platform structure')
def hashes(path):
    before=path.stat()
    sha1=hashlib.sha1(); md5=hashlib.md5(); crc=0; n=0
    with path.open('rb') as f:
        while b:=f.read(4*1024*1024):
            sha1.update(b); md5.update(b); crc=zlib.crc32(b,crc); n+=len(b)
    after=path.stat()
    if n!=before.st_size or n!=after.st_size or before.st_mtime_ns!=after.st_mtime_ns:
        raise ValueError(f'Source changed while hashing: {path}')
    return {'sizeBytes':n,'sha1':sha1.hexdigest(),'md5':md5.hexdigest(),'crc32':f'{crc:08x}'}
def hits(db,h):
    rows=db.execute('SELECT DISTINCT SystemName,GameName,RomName,SizeBytes,SHA1,MD5,CRC FROM RomHashes WHERE lower(SHA1)=? AND SizeBytes=?',(h['sha1'],h['sizeBytes'])).fetchall()
    return [{'system':a,'game':b,'romName':c,'sizeBytes':d,'sha1':e,'md5':f,'crc32':g} for a,b,c,d,e,f,g in rows]
def parse_info(s):
    def get(pattern):
        m=re.search(pattern,s,re.I|re.M)
        return m.group(1).strip() if m else None
    tags=re.findall(r"^\s*Metadata:\s*Tag\s*=\s*['\"](.{4})['\"]",s,re.M)
    logical=get(r'^\s*Logical size:\s*([\d,]+)')
    return {'version':get(r'^\s*File Version:\s*(\d+)'),'mediaType':'DVD-style single image' if tags==['DVD '] else 'Unknown','metadataTags':tags,'logicalSizeBytes':int(logical.replace(',','')) if logical else None,'rawSha1':get(r'^\s*Data SHA1:\s*([0-9a-f]{40})'),'combinedSha1':get(r'^\s*SHA1:\s*([0-9a-f]{40})'),'rawInfo':s}
def summary(rows,failures=0,candidates=0):
    valid=[r for r in rows if r.get('chd')]
    return {'candidateSourceIsos':candidates,'totalSourceIsosTested':len(rows),'chdsSuccessfullyCreated':sum(r['chd']['created'] for r in valid),'chdmanVerifyPass':sum(r['chd']['verify']['exitCode']==0 for r in valid if r['chd'].get('verify')),'chdmanVerifyFail':sum(r['chd']['verify']['exitCode']!=0 for r in valid if r['chd'].get('verify')),'exactRawSha1Matches':sum(r['comparisons'].get('sha1EqualsRawSha1') is True for r in rows),'rawSha1Mismatches':sum(r['comparisons'].get('sha1EqualsRawSha1') is False for r in rows),'exactLogicalSizeMatches':sum(r['comparisons'].get('sizeEqualsLogicalSize') is True for r in rows),'logicalSizeMismatches':sum(r['comparisons'].get('sizeEqualsLogicalSize') is False for r in rows),'exactSha1SizeEquivalenceCases':sum(all(r['comparisons'].get(k) is True for k in ('sha1EqualsRawSha1','sizeEqualsLogicalSize')) for r in rows),'uniqueRedumpSha1SizeMatches':sum(r['redump']['sha1SizeUniqueMatch'] for r in rows),'fullRedumpHashMatches':sum(r['redump']['fullHashMatch'] for r in rows),'ambiguousRedumpMatches':sum(r['redump']['ambiguityCount']>1 for r in rows),'redumpNotFoundCases':sum(r['redump']['ambiguityCount']==0 for r in rows),'classificationFailures':failures}
def verdict(s):
    n=s['totalSourceIsosTested']
    if s['rawSha1Mismatches'] or s['logicalSizeMismatches'] or s['chdmanVerifyFail'] or s['ambiguousRedumpMatches']: return 'CONTRADICTED'
    if n==0 or s['classificationFailures'] or n<2 or s['chdsSuccessfullyCreated']!=n or s['chdmanVerifyPass']!=n or s['exactSha1SizeEquivalenceCases']!=n or s['uniqueRedumpSha1SizeMatches']!=n or s['fullRedumpHashMatches']!=n: return 'INSUFFICIENT EVIDENCE'
    return 'PROVEN'
version=run()
version_text=version['stdout'] or version['stderr']
if not version_text:
    WORK.rmdir()
    raise RuntimeError('Unable to identify bundled chdman version')
report={'tooling':{'chdmanVersion':version_text.splitlines()[0],'campaignDate':datetime.datetime.now(datetime.timezone.utc).isoformat(),'corpusRoot':str(CORPUS),'chdmanPath':str(CHDMAN),'redumpDatabaseSnapshot':str(DB),'temporaryWorkDirectory':str(WORK)},'ps2Dvd':{'summary':{},'files':[]},'psp':{'summary':{},'files':[]},'excluded':[],'stopReason':None,'notes':['CHD RawSha1AndSize is distinct from Full Redump verification. Canonical ISO hashes alone can establish a Full Redump match.','PS2 ISO CD/DVD classification requires authoritative media evidence. BOOT2, filenames, and image size are not used to infer DVD.']}
try:
    db=sqlite3.connect(f'file:{DB.as_posix()}?mode=ro',uri=True)
    for path in sources:
        media,evidence=classify(path)
        print('CLASSIFY',path.name,media,flush=True)
        h=hashes(path)
        matches=hits(db,h)
        red={'system':None,'game':None,'romName':None,'exactSizeBytes':None,'sha1':None,'md5':None,'crc32':None,'sha1SizeUniqueMatch':len(matches)==1,'fullHashMatch':False,'ambiguityCount':len(matches),'mismatchReason':None,'candidates':matches if len(matches)>1 else []}
        if len(matches)==1:
            m=matches[0]; red.update({k:m[k] for k in ('system','game','romName','sha1','md5','crc32')}); red['exactSizeBytes']=m['sizeBytes']
            red['fullHashMatch']=all(str(m[k]).lower()==h[k] for k in ('sha1','md5','crc32')) and m['sizeBytes']==h['sizeBytes']
            if not red['fullHashMatch']: red['mismatchReason']='Redump MD5 and/or CRC32 differs from canonical ISO'
        elif len(matches)==0: red['mismatchReason']='No SHA1 + exact-size entry in local Redump database'
        else: red['mismatchReason']='Ambiguous SHA1 + exact-size identity'
        row={'platform':media,'sourcePath':str(path),'sourceFilename':path.name,'source':h,'classificationEvidence':evidence,'chd':None,'comparisons':{'sha1EqualsRawSha1':None,'sizeEqualsLogicalSize':None},'redump':red}
        if len(matches)>1:
            report['stopReason']=f'Ambiguous Redump SHA1 + size for {path.name}'
            report['excluded'].append(row)
            break
        if media=='PS2-unknown-media':
            report['excluded'].append(row)
            continue
        if media not in ('PSP','PS2-DVD'):
            report['excluded'].append(row)
            report['stopReason']=f'Classification failure: {path.name}'
            break
        key='psp' if media=='PSP' else 'ps2Dvd'
        report[key]['files'].append(row)
        if matches and (matches[0]['system'] != ('Sony - PlayStation Portable' if media=='PSP' else 'Sony - PlayStation 2')):
            report['stopReason']=f'Redump system contradicts internal media identity: {path.name}'
            break
        if matches and not red['fullHashMatch']:
            report['stopReason']=f'Redump canonical ISO hashes contradict source: {path.name}'
            break
        out=WORK/(hashlib.sha1(str(path).encode()).hexdigest()+'.chd')
        try:
            cr=run('createdvd','-i',path,'-o',out)
            row['chd']={'created':cr['exitCode']==0,'create':cr,'info':None,'verify':None}
            if cr['exitCode']!=0:
                report['stopReason']=f'CHD creation failed: {path.name}'
                break
            ir=run('info','-i',out)
            row['chd']['info']=parse_info(ir['stdout']) if ir['exitCode']==0 else {'error':ir}
            vr=run('verify','-i',out)
            row['chd']['verify']=vr
            info=row['chd']['info']
            row['comparisons']={'sha1EqualsRawSha1':h['sha1']==str(info.get('rawSha1','')).lower(),'sizeEqualsLogicalSize':h['sizeBytes']==info.get('logicalSizeBytes')}
            if vr['exitCode']!=0 or 'Raw SHA1 verification successful!' not in vr['stdout'] or info.get('mediaType')!='DVD-style single image' or not all(row['comparisons'].values()):
                report['stopReason']=f'Structural contradiction: {path.name}; verify={vr["exitCode"]}; tags={info.get("metadataTags")}; comparisons={row["comparisons"]}'
                break
            print('PROOF',path.name,row['comparisons'],'verify',vr['exitCode'],'redump',len(matches),flush=True)
        finally:
            out.unlink(missing_ok=True)
except Exception as e:
    report['stopReason']=f'Campaign error: {type(e).__name__}: {e}'
finally:
    for key in ('ps2Dvd','psp'):
        group=report[key]
        count=sum(x['platform']=='PS2-unknown-media' for x in report['excluded']) if key=='ps2Dvd' else 0
        candidates=sum(x['platform']=='PS2-unknown-media' for x in report['excluded']) if key=='ps2Dvd' else len(group['files'])
        group['summary']=summary(group['files'],count,candidates)
        group['verdict']='INSUFFICIENT EVIDENCE' if key=='ps2Dvd' else verdict(group['summary'])
        if key=='psp' and group['verdict']=='PROVEN':
            group['verdict']='CORPUS_PROVEN / CANDIDATE'
        if report['stopReason'] and ('Structural contradiction' in report['stopReason'] or 'Ambiguous Redump' in report['stopReason']):
            if report[key]['files'] and report['stopReason'].split(': ')[-1].split(';')[0] in [r['sourceFilename'] for r in report[key]['files']]:
                group['verdict']='CONTRADICTED'
    REPORT.parent.mkdir(parents=True,exist_ok=True)
    temporary_report=REPORT.with_name(REPORT.name+'.'+os.urandom(8).hex()+'.tmp')
    try:
        with temporary_report.open('x',encoding='utf-8') as output:
            json.dump(report,output,indent=2,ensure_ascii=False)
            output.write('\n')
        if REPORT.exists():
            raise FileExistsError(f'Report already exists: {REPORT}')
        temporary_report.rename(REPORT)
    finally:
        temporary_report.unlink(missing_ok=True)
        WORK.rmdir()
    print('REPORT',REPORT,'STOP',report['stopReason'],'VERDICTS',report['ps2Dvd']['verdict'],report['psp']['verdict'],flush=True)
