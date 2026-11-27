"""Convert authored TDF section graphs to a continuous Reflex lap and start chute."""
import math,struct
import numpy as np

def payload(course,center,sample,kind=0):
    sections=course['sections'];order=[];seen={};current=0
    while current not in seen:
        if current not in sections:raise ValueError('Broken source section link')
        seen[current]=len(order);order.append(current)
        links=sections[current]['links']
        if not links:raise ValueError('Source route is point-to-point')
        current=links[min(links)]
    split=seen[current]
    def concat(ids):
        points=[]
        for sid in ids:
            part=sections[sid]['points']
            if points and np.linalg.norm(np.array(points[-1]['centre'])-part[0]['centre'])>2:raise ValueError('Disconnected source sections')
            points.extend(part[1:] if points else part)
        return points
    raw=concat(order[split:])
    if len(raw)<4 or np.linalg.norm(np.array(raw[0]['centre'])-raw[-1]['centre'])>2:raise ValueError('Source route has no closed circuit')
    def point(p):
        x,_,z=p['centre'];width=np.linalg.norm(np.array(p['right'])-p['left'])
        return [x-center[0],float(sample(x,z)),z-center[1],float(np.clip(width,10,300)),45.0]
    loop=[point(p) for p in raw[:-1]]
    finish=course['gates']['finish_gate'];target=np.array(finish.pos)[[0,2]]-center
    k=min(range(len(loop)),key=lambda i:np.linalg.norm(np.array(loop[i])[[0,2]]-target))
    lap=loop[k:]+loop[:k]+[loop[k]]
    if split:
        lead=[point(p) for p in concat(order[:split])]+loop[1:k+1]
    else:
        start=np.array(course['gates']['start_gate'].pos)[[0,2]]-center
        j=min(range(len(loop)),key=lambda i:np.linalg.norm(np.array(loop[i])[[0,2]]-start))
        lead=loop[j:k+1] if j<k else loop[j:]+loop[:k+1]
        if len(lead)<2:lead=[loop[(k-1)%len(loop)],loop[k]]
    # The track's logical start/finish planes exactly meet its exported sections.
    def dense(points):
        out=[]
        for a,b in zip(points,points[1:]):
            d=math.hypot(b[0]-a[0],b[2]-a[2])
            if d<.01:continue
            for i in range(max(1,math.ceil(d/10))):
                n=max(1,math.ceil(d/10));out.append([x+(y-x)*i/n for x,y in zip(a,b)])
        return out+[points[-1]]
    lap,lead=dense(lap),dense(lead)
    if max(len(lap),len(lead))>4096:raise ValueError('Race route too long')
    gates=[]
    for points in (lead,lap):
        a,b=points[0],points[1];d=np.array([b[0]-a[0],0,b[2]-a[2]]);d/=np.linalg.norm(d)
        gates.extend([*a[:3],*d,-d[2],0,d[0],a[3]])
    return struct.pack('<4s4I20f',b'RXRA',1,kind,len(lap),len(lead),*gates)+b''.join(struct.pack('<5f',*p) for p in lap+lead)

def assets(source,track,center,sample):
    import pak,tdf
    p=pak.Pak(source/'Game Files/data'/f'{track}.pak')
    try:
        name=f'Data/Env/{track}/{track}.tdf'
        if not any(e.name.lower().replace('\\','/')==name.lower() for e in p.entries):return None,'No authored race route'
        course=tdf.parse(p.read(name))
        return payload(course,center,sample,1 if track.startswith('SX') else 0),None
    except ValueError as e:return None,str(e)
    finally:p.close()
