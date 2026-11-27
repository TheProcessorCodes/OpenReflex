"""Source scenery conversion using Open Unleashed's original parsers and texture decoder."""
import configparser, ctypes as C, pathlib, struct, json
from functools import lru_cache
import numpy as np


def convert(source, center, track="Nat17", extent=5760, vegetation_out=None, reflex_archive=None):
    import pak, dxg, col, esb
    archives=[];files={}
    for path in sorted((source/'Game Files/data').glob('*.pak')):
        archive=pak.Pak(path);archives.append(archive)
        for entry in archive.entries:
            files.setdefault(entry.name.lower().replace('\\','/'),(archive,entry))
    @lru_cache(maxsize=512)
    def read(path):
        archive,entry=files[path.lower().replace('\\','/')];return archive.read(entry)
    @lru_cache(maxsize=4096)
    def resolve(name,extension):
        base=pathlib.PurePosixPath(name.replace('\\','/')).name.lower()
        if '.' in base:base=base.rsplit('.',1)[0]
        candidates=[p for p in files if pathlib.PurePosixPath(p).name==base+extension]
        if not candidates:raise FileNotFoundError(name+extension)
        return candidates[0]
    lib=C.CDLL(str(source.resolve()/'native/build/libmxacore.dylib'))
    def bind(name,restype,args):
        f=getattr(lib,name);f.restype=restype;f.argtypes=args;return f
    parse=bind('mxa_dxt_parse',C.c_void_p,[C.c_void_p,C.c_int64])
    info=bind('mxa_dxt_info_get',C.c_int,[C.c_void_p,C.c_void_p])
    decode=bind('mxa_dxt_decode_rgba',C.c_int,[C.c_void_p,C.c_int,C.c_void_p,C.c_int])
    close=bind('mxa_dxt_close',None,[C.c_void_p])
    textures=[];materials=[];draws=[];known={};missing=[];missing_collision=[];missing_textures=[]
    fx=[];notes={};decoded={}
    def note(text):notes[text]=notes.get(text,0)+1
    MATERIAL='<IBB??B???3f?2fB';LAYER='<IBB??2fB??3f'
    def add_texture(w,h,rgba):
        textures.append(struct.pack('<Iii',len(textures)+1,w,h)+rgba);return len(textures)
    def decode_dxt(b,what):
        ptr=parse(b,len(b))
        if not ptr:raise ValueError('Cannot decode '+what)
        try:
            inf=(C.c_int*8)();assert info(ptr,inf)==0
            w,h=inf[2],inf[3];rgba=C.create_string_buffer(w*h*4);assert decode(ptr,0,rgba,len(rgba))==len(rgba)
            return w,h,rgba.raw
        finally:close(ptr)
    def atx_frames(b,what):
        # .atx: u32 1, u32 frames, u32, then whole .dxt files (12 + palette x 4 + 12 + per mip 12 + size)
        if len(b)<12 or struct.unpack_from('<I',b)[0]!=1:raise ValueError('Invalid animated texture '+what)
        count=struct.unpack_from('<I',b,4)[0];o=12;frames=[]
        for _ in range(count):
            if o+24>len(b):break
            q=o+12+struct.unpack_from('<I',b,o+8)[0]*4;mips=struct.unpack_from('<I',b,q)[0];q+=12
            for _ in range(mips):q+=12+struct.unpack_from('<I',b,q+8)[0]
            if q>len(b):break
            frames.append(b[o:q]);o=q
        if not frames:raise ValueError('Empty animated texture '+what)
        return frames
    def texture(name,fallback=None,animated=False):
        """(id, has alpha, frame ids) of a pass's texture, by the game's order of file types (.dxt, .atx)."""
        key=(name.lower(),animated)
        if key in decoded:return decoded[key]
        frames=None
        if name.startswith('&') or name.lower() in ('framebuffer','displaybuffer'):
            # A render target (the stadium screens). The mesh texture's own image stands in when the source has one.
            if fallback and fallback.lower()!=name.lower():
                try:resolve(fallback,'.dxt');decoded[key]=texture(fallback);note('render target shown as its still image: '+fallback);return decoded[key]
                except FileNotFoundError:pass
            note('render target (a live screen in the original): dark panel');tid=add_texture(1,1,bytes((32,32,32,255)));result=(tid,False,None)
        else:
            try:w,h,raw=decode_dxt(read(resolve(name,'.dxt')),name);ids=[add_texture(w,h,raw)]
            except FileNotFoundError:
                try:blobs=atx_frames(read(resolve(name,'.atx')),name)
                except FileNotFoundError:
                    missing_textures.append(name);tid=add_texture(1,1,bytes((255,255,255,255)));decoded[key]=(tid,False,None);return decoded[key]
                ids=[]
                for i,blob in enumerate(blobs if animated else blobs[:1]):
                    w,h,raw=decode_dxt(blob,name)
                    if i==0:first=raw
                    ids.append(add_texture(w,h,raw))
                raw=first;frames=ids if len(ids)>1 else None
            alpha=bool(np.any(np.frombuffer(raw,dtype=np.uint8)[3::4]<255));result=(ids[0],alpha,frames)
        decoded[key]=result;return result
    def number(value,default=0.0):
        try:return float(str(value).split(',')[0])
        except (TypeError,ValueError):return default
    def truth(value,default):
        c=str(value).strip().lower()[:1] if value is not None else ''
        return True if c in ('t','1','y') else False if c in ('f','0','n') else default
    def pass_state(shader,name,first):
        """One [ShaderPass_N] as the game's material loader reads it: every key from the mesh template (DfltMT.dxm: prelit, alpha
        test > 0, no blend) unless the pass sets it. Animators: a frame animation needs the .atx; a UV animation only runs on
        the *TexTransform vertex shaders with a VertexAnimation (the PC build)."""
        bound=shader.get('texturemap_1') or name
        vs=(shader.get('vertexshadername') or 'DXVPrelit.xvu').lower()
        frame=None;uv=None
        for k in range(1,int(number(shader.get('numanimators'),0))+1):
            sec='ShaderPass_%d_Animator_%d'%(first,k)
            if not recipe_of[name].has_section(sec):continue
            a=recipe_of[name][sec];alg=(a.get('algorithm') or '').strip().strip('"').lower()
            if alg=='textureframe' and a.get('name') is None:   # named ones are driven by the race (holeshot / leader lights)
                frame=dict(fps=number(a.get('animatedmapframespersecond'),1),initial=number(a.get('animatedmapinitialframe'),0),looping=int(number(a.get('animatedmaplooping'),1))!=0,
                           reverse=int(number(a.get('animatedmapreverselooponend'),0))!=0,loopTo=int(number(a.get('animatedmaplooponendtoframe'),-1)))
            elif alg=='uvanimation' and 'textransform' in vs and (shader.get('vertexanimation') or 'none').strip().strip('"').lower()!='none':
                uv=dict(angle=number(a.get('angle')),rate=number(a.get('uvangularrate')),uCenter=number(a.get('ucenter')),vCenter=number(a.get('vcenter')),uScale=number(a.get('uscale'),1),vScale=number(a.get('vscale'),1),
                        uShear=number(a.get('ushear')),vShear=number(a.get('vshear')),uMax=number(a.get('umax'),1) or 1,vMax=number(a.get('vmax'),1) or 1,uVel=number(a.get('uvelocity')),vVel=number(a.get('vvelocity')))
        tid,alpha,frames=texture(bound,name,frame is not None)
        blend_on=truth(shader.get('alphablendenable'),False)
        additive=blend_on and (shader.get('destblend') or '').lower()=='d3dblend_one'
        # the alpha test: GREATER ref unless the pass says otherwise (the template's is "> 0": only clear texels are dropped)
        test=truth(shader.get('alphatestenable'),True);func=(shader.get('alphafunc') or 'd3dcmp_greater').lower();ref=int(number(shader.get('alpharef'),0))
        cut=0 if not test or func.endswith('always') else 255 if func.endswith('never') else max(ref,1) if func.endswith('greaterequal') else min(ref+1,255)
        return dict(texture=tid,alpha=alpha,frames=frames if frame else None,frame=frame,uv=uv,blend=1 if additive else 5 if blend_on else 0,additive=additive,cutoff=cut if alpha else 0,
                    clampU='clamp' in (shader.get('addressu_0') or '').lower(),clampV='clamp' in (shader.get('addressv_0') or '').lower(),
                    uv1='1' in (shader.get('texcoordindex_0') or '').split('|')[0],vs=vs)
    recipe_of={}
    def material(name):
        """The scenery material of a mesh texture name: pass 1 as the base, the later passes (each its own draw of the same
        triangles in the original) as layers over it. Vertex colours are stored as authored (0x80 = 1.0): the pass colour is 2."""
        if name in known:return known[name]
        recipe=configparser.ConfigParser(strict=False,interpolation=None,allow_no_value=True)
        try:recipe.read_string(read(resolve(name,'.dxm')).decode('latin1'))
        except FileNotFoundError:pass
        recipe_of[name]=recipe
        states=[]
        for n in range(1,max(1,int(number(recipe.get('General','TotalShaderPasses',fallback='1'),1)))+1):
            sec='ShaderPass_%d'%n
            if not recipe.has_section(sec):
                if n==1:states.append(pass_state({},name,n))
                continue
            shader=recipe[sec]
            if int(number(shader.get('enablethisshader'),1))==0:continue
            # the LOD passes: drawn from MaterialPassStart on; the pass at distance 0 is the one a rider sees
            if states and number(shader.get('materialpassstart'),0)>0:note('distance (LOD) material pass left out');continue
            states.append(pass_state(shader,name,n))
        if not states:states.append(pass_state({},name,1))
        base=states[0];layers=b''
        index=len(materials)
        if base['frames']:fx.append(dict(material=index,stage=0,frames=base['frames'],**base['frame']))
        if base['uv']:fx.append(dict(material=index,stage=0,uv=base['uv']))
        count=0
        for stage,p in enumerate(states[1:4],1):
            if not p['blend']:note('opaque second material pass left out');continue
            count+=1
            # set 1 holds the mesh's second UVs, set 2 the first (a layer cannot address set 0)
            layers+=struct.pack(LAYER,p['texture'],p['blend'],128,False,False,1,1,1 if p['uv1'] else 2,p['clampU'],p['clampV'],2,2,2)
            if p['frames']:fx.append(dict(material=index,stage=count,frames=p['frames'],**p['frame']))
            if p['uv']:fx.append(dict(material=index,stage=count,uv=p['uv']))
        materials.append(struct.pack(MATERIAL,base['texture'],base['blend'],base['cutoff'],base['alpha'],base['additive'],128,False,base['clampU'],base['clampV'],2,2,2,False,1,1,count)+layers)
        known[name]=index;return index
    @lru_cache(maxsize=512)
    def model(name): return dxg.parse(read(resolve(name,'.dxg')))
    @lru_cache(maxsize=512)
    def collider(name): return col.parse(read(resolve(name,'.col')))
    scn=configparser.ConfigParser(strict=False,interpolation=None,allow_no_value=True);scn.read_string(read(f'Data/Env/{track}/{track}.scn').decode('latin1'))
    def decoded_model(name, pos, rotation):
        mesh=model(name);result=[]
        for surf in mesh['groups'][0]['surfaces']:
            quant=surf.get('quant',{});vertices=np.array(dxg.decode(*surf['attrs'][0],quant.get('pos_scale',1)))
            if 4 in surf['attrs']:
                bone=np.array(dxg.decode(*surf['attrs'][4])).reshape(-1).astype(int)
                palettes=mesh.get('palettes',[]);pi=surf.get('palette') or 0
                palette=palettes[pi if pi<len(palettes) else 0] if palettes else list(range(len(mesh['nodes'])))
                matrices=[]
                for node in mesh['nodes']:
                    matrix=np.array(node['matrix']).reshape(4,4)
                    if node['parent']>=0:matrix=matrix@matrices[node['parent']]
                    matrices.append(matrix)
                for slot in np.unique(bone):
                    take=bone==slot
                    node=palette[slot] if 0<=slot<len(palette) else -1
                    if not 0<=node<len(matrices):continue # source renderer leaves unbound vertices in model space
                    matrix=matrices[node]
                    vertices[take]=(np.einsum("ij,jk->ik",np.c_[vertices[take],np.ones(take.sum())],matrix))[:,:3]
            vertices=np.einsum("ij,jk->ik",vertices,rotation)+pos
            if not np.isfinite(vertices).all():raise ValueError("Invalid scenery geometry: "+name)
            uv=np.array(dxg.decode(*surf['attrs'][3],quant.get('uv_scale',1))) if 3 in surf['attrs'] else np.zeros((len(vertices),2))
            uv1=np.array(dxg.decode(*surf['attrs'][10],quant.get('uv_scale',1))) if 10 in surf['attrs'] else uv
            if uv1.shape!=uv.shape:uv1=uv
            indices=np.array(dxg.strip_tris(surf['indices'],surf['nverts']),dtype='<i4')
            if not indices.size:continue
            tex=mesh['textures'][surf['material']]
            try:mi=material(tex)
            except FileNotFoundError:missing.append(tex);continue
            # the stored D3DCOLOR as it is (0x80 = 1.0, brighter above: not clamped); no colour = 1.0
            colors=np.full((len(vertices),4),128,dtype=np.uint8);colors[:,3]=255
            if 2 in surf['attrs'] and surf['attrs'][2][0]==0x40:
                packed=np.frombuffer(surf['attrs'][2][1],dtype='<u4')
                for channel,shift in enumerate((16,8,0)):colors[:,channel]=(packed>>shift)&255
                colors[:,3]=(packed>>24)&255
            result.append((mi,vertices,(uv,uv1),indices,colors))
        return result

    def emit_draw(mi,vertices,uv,indices,colors=None,group=-1,sky=False,into=None):
        uv0,uv1=uv if isinstance(uv,tuple) else (uv,uv)
        raw=bytearray(len(vertices)*48)
        for i,v in enumerate(vertices):struct.pack_into('<11f4B',raw,i*48,*v,*uv0[i],*uv1[i],*uv0[i],*uv0[i],*(colors[i] if colors is not None else (128,128,128,255)))
        (draws if into is None else into).append(struct.pack('<i??iii',mi,sky,False,group,len(vertices),indices.size)+raw+indices.tobytes())

    from scene_rules import hidden_models
    import startgate
    hidden,hidden_collision,hidden_reasons=hidden_models(scn,race_gate=startgate.races(track))
    models=0;special=[];manifest=[]
    import funnel as funnel_import
    funnel_cfg=funnel_import.sections(track,files,read) if startgate.races(track) else None   # (the start's bale sets: race levels)
    funnel_parts=[[],[]];funnel_tris=[[],[]]
    for sec in scn.sections():
        if not sec.lower().startswith('model') or 'slt' not in scn[sec]:continue
        if sec in hidden:continue
        obj=scn[sec];name=obj['slt'];mesh=model(name)
        pos=np.array([float(x) for x in obj.get('position','0,0,0').split(',')]);pos[[0,2]]-=center
        forward=np.array([float(x) for x in obj.get('lookvector','0,0,1').split(',')]);forward/=np.linalg.norm(forward)
        up=np.array([float(x) for x in obj.get('upvector','0,1,0').split(',')]);right=np.cross(up,forward);right/=np.linalg.norm(right);up=np.cross(forward,right);rotation=np.array([right,up,forward])
        kind=(obj.get('type') or '').strip().lower()
        fset=funnel_import.which(funnel_cfg,name)
        if fset is not None:
            funnel_parts[fset]+=decoded_model(name,pos,rotation);funnel_tris[fset].append(funnel_import.triangles(collider,name,pos,rotation))
            manifest.append(dict(section=sec,slt=name,mode='funnel '+funnel_import.NAMES[fset]));models+=1;continue
        if kind=='camerafollowing' and (truth(obj.get('followcamera'),False) or truth(obj.get('facecamera'),False)):
            # CameraFollowingModel: it sits at the camera's eye (FollowCamera) or turns to it where it stands (FaceCamera)
            if truth(obj.get('followcamera'),False):
                if number(obj.get('maxtiltangle'),0)>0:
                    manifest.append(dict(section=sec,slt=name,mode='dropped',reason='the camera-following rain: the weather draws rain'));continue
                parts=decoded_model(name,np.zeros(3),rotation)
                for part in parts:emit_draw(*part,sky=True,into=special)
                manifest.append(dict(section=sec,slt=name,mode='sky',draws=len(parts)));models+=1;continue
            parts=decoded_model(name,np.zeros(3),np.eye(3))
            for mi,vertices,uv,indices,colors in parts:
                raw=bytearray()
                for i,v in enumerate(vertices):raw+=struct.pack('<11f4B5f',*(pos+v),*uv[0][i],*uv[1][i],*uv[0][i],*uv[0][i],*colors[i],*pos,v[0],v[1])
                special.append(struct.pack('<i??iii',mi,False,True,-1,len(vertices),indices.size)+bytes(raw)+indices.tobytes())
            manifest.append(dict(section=sec,slt=name,mode='billboard',draws=len(parts)));models+=1;continue
        parts=decoded_model(name,pos,rotation)
        for decoded_part in parts:emit_draw(*decoded_part)
        manifest.append(dict(section=sec,slt=name,mode='placed',draws=len(parts)))
        models+=1
    placed_draws=len(draws);draws+=special
    eco_name=scn.get('Environment','EcosystemFile',fallback='')
    eco_path=f'Data/Env/{track}/{pathlib.PureWindowsPath(eco_name).stem}.esb'
    ecosystem=esb.parse(read(eco_path)) if eco_name and eco_path.lower() in files else {'species': [], 'instances': []}
    tree_groups=[];tree_instances=[];tree_triangles=0
    if vegetation_out is not None:
        for si,species in enumerate(ecosystem['species']):
            parts=decoded_model(species['mesh'],np.zeros(3),np.eye(3))
            if not parts:raise ValueError('Empty ecosystem model '+species['mesh'])
            vertices=np.concatenate([part[1] for part in parts]);lo=vertices.min(0);hi=vertices.max(0)
            height=hi[1]-lo[1];radius=max(abs(lo[0]),abs(hi[0]),abs(lo[2]),abs(hi[2]))
            if height<=0 or radius<=0:raise ValueError('Invalid ecosystem model bounds '+species['mesh'])
            tree_groups.append('UnleashedTree:'+str(si))
            for mi,vertices,uv,indices,colors in parts:
                vertices=vertices.copy();vertices[:,1]-=lo[1];vertices/=np.array([radius,height,radius])
                emit_draw(mi,vertices,uv,indices,colors,si);tree_triangles+=indices.size//3
        # Match the source loader: instances marked snap take the current terrain height.
        sample,_,_=esb.terrain_sampler(read(f'Data/Env/{track}/{track}.dx.trn'))

    replaced=set()
    if reflex_archive is not None:
        from reflex_scenery import plan
        selection=plan(track)
        if selection:replaced={i for i,s in enumerate(ecosystem['species']) if s['mesh'].lower() in selection['species']}
    vegetation=0; plants={}
    for inst in ecosystem['instances']:
        si,x,y,z,height,radius=esb.inst_world(ecosystem,inst,extent)
        species=ecosystem['species'][si]
        if vegetation_out is not None:
            if inst[1]:
                ground=sample(x,z)
                if ground is not None:y=ground
            tree_instances.append(dict(species=si,x=float(x-center[0]),y=float(y),z=float(z-center[1]),height=float(height),radius=float(radius)))
        if si in replaced:continue
        mi=material(species['billboard']);x-=center[0];z-=center[1]
        positions=[(x-radius,y,z),(x+radius,y,z),(x+radius,y+height,z),(x-radius,y+height,z)]
        uv=[(species['u_left'],species['v_bottom']),(species['u_right'],species['v_bottom']),(species['u_right'],species['v_top']),(species['u_left'],species['v_top'])]
        raw=bytearray()
        for i,v in enumerate(positions):
            offset=(-radius if i in (0,3) else radius,0 if i<2 else height)
            raw+=struct.pack('<11f4B5f',*v,*uv[i],*uv[i],*uv[i],*uv[i],128,128,128,255,x,y,z,*offset)
        plant=plants.setdefault(mi,[bytearray(),[]]);base=len(plant[0])//68;plant[0]+=raw;plant[1].extend([base+i for i in (0,2,1,0,3,2)]);vegetation+=1
    for mi,(raw,indices) in plants.items():
        draws.append(struct.pack('<i??iii',mi,False,True,-1,len(raw)//68,len(indices))+raw+np.array(indices,dtype='<i4').tobytes())
    # the .toy's animated props (the ferris wheel, the stadium finish arch, windmills ...): hidden part groups + baked motion
    import toys as toy_import
    toy_groups,toy_data,toy_report=toy_import.convert(track,files,read,center,material,emit_draw,len(tree_groups))
    funnel_groups=[];funnel_data=None
    if funnel_cfg and (funnel_parts[0] or funnel_parts[1]):
        for k in range(2):
            funnel_groups.append('UnleashedFunnel:'+funnel_import.NAMES[k])
            for part in funnel_parts[k]:emit_draw(*part,group=len(tree_groups)+len(toy_groups)+k)
        funnel_data=funnel_import.pack(funnel_cfg,center,funnel_tris)
    collision=[]
    for sec in scn.sections():
        if not sec.lower().startswith('object') or 'col' not in scn[sec]:continue
        obj=scn[sec]
        if any(name in obj['col'].lower() for name in hidden_collision):continue
        try:shape=collider(obj['col'])
        except FileNotFoundError:
            missing_collision.append(obj['col']);continue
        pos=np.array([float(x) for x in obj.get('position','0,0,0').split(',')]);pos[[0,2]]-=center
        forward=np.array([float(x) for x in obj.get('lookvector','0,0,1').split(',')]);forward/=np.linalg.norm(forward)
        up=np.array([float(x) for x in obj.get('upvector','0,1,0').split(',')]);right=np.cross(up,forward);right/=np.linalg.norm(right);up=np.cross(forward,right);rotation=np.array([right,up,forward])
        for part in shape.get('children',[shape]):
            if 'verts' not in part:raise ValueError('Unsupported collision type')
            vertices=np.frombuffer(part['verts'],'<i2' if part['compressed'] else '<f4').reshape(-1,3).astype(float)
            matrix=np.array(part['matrix']).reshape(4,3)
            vertices=np.einsum("ij,jk->ik",np.einsum("ij,jk->ik",vertices,matrix[:3])+matrix[3],rotation)+pos
            indices=np.array([t[4:7] for t in part['tris']],dtype=int)
            points=vertices[indices].reshape(-1,3).astype('<f4')
            if not np.isfinite(points).all():raise ValueError('Invalid collision geometry')
            for start in range(0,len(points),48000):
                chunk=points[start:start+48000];collision.append(struct.pack('<I',len(chunk))+chunk.tobytes())
    for archive in archives:archive.close()
    if missing:raise ValueError('Unresolved material textures: '+', '.join(sorted(set(missing))))
    def dotnet_string(value):
        b=value.encode();n=len(b);prefix=bytearray()
        while n>=128:prefix.append((n&127)|128);n>>=7
        prefix.append(n);return bytes(prefix)+b
    groups=[dotnet_string(name)+struct.pack('??',True,False) for name in tree_groups+toy_groups]+[dotnet_string(name)+struct.pack('??',False,False) for name in funnel_groups]
    if vegetation_out is not None:
        vegetation_out.parent.mkdir(parents=True,exist_ok=True)
        if reflex_archive is not None:
            from reflex_scenery import write
            actual=write(reflex_archive,vegetation_out.parent,track,tree_instances,ecosystem['species'])
            assert actual==replaced
        vegetation_out.write_text(json.dumps(dict(version=1,groups=tree_groups,instances=[p for p in tree_instances if p['species'] not in replaced])))
        (vegetation_out.parent/'scenery-fx.json').write_text(json.dumps(dict(version=1,materials=fx)))
        toy_file=vegetation_out.parent/'toys.rxtoys'
        if toy_data:toy_file.write_bytes(toy_data)
        elif toy_file.exists():toy_file.unlink()
        funnel_file=vegetation_out.parent/'funnel.rxfunnel'
        if funnel_data:funnel_file.write_bytes(funnel_data)
        elif funnel_file.exists():funnel_file.unlink()
        (vegetation_out.parent/'scenery-manifest.json').write_text(json.dumps(dict(version=1,rules=2,placedDraws=placed_draws,models=manifest,notes=notes)))
    out=struct.pack('<4sI',b'RXSC',1)
    for collection in (textures,materials,groups,draws):out+=struct.pack('<I',len(collection))+b''.join(collection)
    return out,struct.pack('<4sII',b'RXCO',1,len(collision))+b''.join(collision),dict(hiddenModels=hidden_reasons,models=models,draws=len(draws),textures=len(textures),collisionChunks=len(collision),vegetation=vegetation,reflexVegetation=sum(p['species'] in replaced for p in tree_instances),treeModels=len(tree_groups),treeModelTriangles=tree_triangles,missingSourceCollision=sorted(set(missing_collision)),missingSourceTextures=sorted(set(missing_textures)),materialRules=2,animatedMaterials=len(fx),animatedProps=toy_report,funnel=[len(funnel_tris[0]),len(funnel_tris[1])] if funnel_data else None,notes=notes)
