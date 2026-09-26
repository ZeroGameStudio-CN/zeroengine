import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('migration', Path(__file__).with_name('prepare_migration.py'))
migration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(migration)

class MigrationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name) / 'project'
        self.directory = self.root / 'docs/architecture/project-atlas'
        self.directory.mkdir(parents=True)
        self.catalog = self.root / 'docs/architecture/project-atlas.json'
        self.fragment = self.directory / 'source.json'
        self.catalog.write_text(json.dumps({'schemaVersion':1,'project':{'id':'fixture'},'sources':['docs/architecture/project-atlas/source.json']}))
        self.data={'schemaVersion':1,'references':[{'id':'module.a','target':'a'},{'id':'module.b','target':'b'}],
                   'systems':[{'id':'system','program':{'entryRefs':['module.b','module.a'],'structureRefs':['module.a'],'dataFlow':['keep narrative']},'agent':{'changeBoundary':'keep ownership'}}]}
        self.fragment.write_text(json.dumps(self.data))
    def tearDown(self): self.tmp.cleanup()
    def test_preserves_data_and_input_bytes(self):
        before=(self.catalog.read_bytes(),self.fragment.read_bytes())
        out=Path(self.tmp.name)/'out'
        migration.prepare(self.root,out)
        refs=[];systems=[];contributions=[]
        for path in out.glob('docs/architecture/project-atlas/**/*.atlas.json'):
            d=json.loads(path.read_text());refs+=d.get('references',[]);systems+=d.get('systems',[]);contributions+=d.get('contributions',[])
        for contribution in contributions:
            target=next(s for s in systems if s['id']==contribution['systemId'])['program']
            for key in ('entryRefs','structureRefs','verificationRefs'):
                if key in contribution: target.setdefault(key,[]).extend(contribution[key])
        self.assertEqual(sorted(refs,key=lambda x:x['id']),self.data['references'])
        expected=copy.deepcopy(self.data['systems'])
        for group in (systems,expected):
            for system in group:
                for key in ('entryRefs','structureRefs','verificationRefs'):
                    if key in system['program']: system['program'][key]=sorted(system['program'][key])
        self.assertEqual(systems,expected)
        self.assertEqual(before,(self.catalog.read_bytes(),self.fragment.read_bytes()))
    def test_refuses_project_output(self):
        with self.assertRaises(ValueError): migration.prepare(self.root,self.root/'migration')
    def test_refuses_existing_output(self):
        with self.assertRaises(ValueError): migration.prepare(self.root,Path(self.tmp.name))
    def test_rejects_duplicate_ids(self):
        self.data['references'].append(self.data['references'][0]);self.fragment.write_text(json.dumps(self.data))
        with self.assertRaises(ValueError): migration.prepare(self.root,Path(self.tmp.name)/'out')
    def test_rejects_unknown_fields(self):
        self.data['unknown']='keep';self.fragment.write_text(json.dumps(self.data))
        with self.assertRaises(ValueError): migration.prepare(self.root,Path(self.tmp.name)/'out')
    def test_rejects_source_traversal(self):
        d=json.loads(self.catalog.read_text());d['sources']=['../outside.json'];self.catalog.write_text(json.dumps(d))
        with self.assertRaises(ValueError): migration.prepare(self.root,Path(self.tmp.name)/'out')

if __name__ == '__main__': unittest.main()
