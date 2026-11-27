"""Regression checks for source visibility and branching circuit conversion."""
import configparser,struct,unittest
import numpy as np
from scene_rules import hidden_models
from racing import payload

class ImportRules(unittest.TestCase):
    def test_hidden_challenges_remove_only_their_collision(self):
        c=configparser.ConfigParser();c.read_string('''[Model1]
SLT=Zone_Platform
Type=StartZone
HasCOL=True
[Model2]
SLT=HillFlag
Type=ActivitySpecific
Activity=Climb01
HasCOL=False
[Model3]
SLT=NationalMarker
Type=ActivitySpecific
Activity=Glamis National
[Model4]
SLT=CameraFlash01
[Model5]
SLT=PhysicalWall
CollisionOnly=True
''')
        hidden,collision,_=hidden_models(c)
        self.assertEqual(hidden,{'Model1','Model2','Model4','Model5'})
        self.assertEqual(collision,{'zone_platform'})
    def test_linked_circuit_and_chute_join_at_finish(self):
        def pt(x,z):return dict(centre=(x,0,z),left=(x-10,0,z),right=(x+10,0,z))
        sections={0:dict(points=[pt(-20,0),pt(0,0)],links={0:1}),1:dict(points=[pt(0,0),pt(200,0),pt(200,200)],links={0:2}),2:dict(points=[pt(200,200),pt(0,200),pt(0,0)],links={0:1})}
        class Gate:pass
        g=Gate();g.pos=(200,0,0)
        b=payload(dict(sections=sections,gates={'finish_gate':g}),np.zeros(2),lambda x,z:10,1)
        _,version,kind,n,m=struct.unpack_from('<4s4I',b)
        p=np.frombuffer(b,dtype='<f4',offset=100).reshape(-1,5)
        self.assertEqual((version,kind),(1,1));np.testing.assert_allclose(p[0],p[n-1]);np.testing.assert_allclose(p[0],p[-1]);self.assertGreater(m,2)
        self.assertTrue(np.all(np.linalg.norm(np.diff(p[:n,:3],axis=0),axis=1)<=10.01))
    def test_open_route_is_not_silently_closed(self):
        p=lambda x:dict(centre=(x,0,0),left=(x,0,-10),right=(x,0,10))
        with self.assertRaisesRegex(ValueError,'point-to-point'):
            payload(dict(sections={0:dict(points=[p(0),p(200)],links={})}),np.zeros(2),lambda x,z:0)
if __name__=='__main__':unittest.main()
