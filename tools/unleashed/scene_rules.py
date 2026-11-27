"""Source activity visibility for a clean riding/racing venue (scene_api.c rules)."""
def enabled(value,default=True):
    return str(value).strip().lower() not in ('0','false','f','no') if value is not None else default

def hidden_models(scene,race_gate=False):
    """race_gate: the level starts from Reflex's own gate (startgate.py): the source's gate and its collision are left out."""
    from startgate import SOURCE_MODELS
    hidden=set();collision_names=set();reasons={}
    for section,obj in scene.items():
        if not section.lower().startswith('model') or 'slt' not in obj:continue
        name=obj['slt'];low=name.lower();kind=obj.get('type','').lower()
        reason=None
        activity=obj.get('activity','').lower()
        race_activity=any(k in activity for k in ('national','supercross','supermoto','short track')) and 'waypoint' not in activity
        if race_gate and low in SOURCE_MODELS:reason='source start gate (the Reflex gate stands there)'
        elif kind in ('startzone','waypoint') or (kind=='activityspecific' and not race_activity):reason='activity/challenge object'
        elif kind=='activityspecific' and enabled(obj.get('invisible'),False):reason='invisible race object'
        elif enabled(obj.get('collisiononly'),False):reason='collision-only model'
        elif not enabled(obj.get('startenabled'),True):reason='disabled source model'
        elif 'cameraflash' in low:reason='inactive camera flash'
        elif 'seats' in low and any('crowd' in o.get('slt','').lower() for o in scene.values()):reason='empty-seat crowd alternative'
        if reason:
            hidden.add(section);reasons[reason]=reasons.get(reason,0)+1
            if kind in ('startzone','waypoint','activityspecific') and enabled(obj.get('hascol'),True):collision_names.add(low)
            if reason.startswith('source start gate'):collision_names.add(low)
    return hidden,collision_names,reasons
