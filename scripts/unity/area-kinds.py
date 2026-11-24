#!/usr/bin/env python3
"""Each map's areas get a kind (village / farm / factory / market / lodge / town / city / outpost / hill): the colour and
the small word under the area's name over the world (AreaLabels.cs). Run after a map is baked / exported again:

    python3 scripts/unity/area-kinds.py
"""
import json
R = 'unity/Assets/LastWar/Resources/Maps/'
KINDS = {
    'green': {'Old Mill': 'farm', 'Hayfield Farm': 'farm', 'West Farm': 'farm', 'Stonewall': 'village', 'Millbrook': 'village',
              'Moorgate': 'village', 'Brookend': 'village', 'Granite Tor': 'hill', 'Low Tor': 'hill'},
    'classic': {n: 'village' for n in ['Moorgate', 'Brookend', 'Larkhill', 'Pine Hollow', 'Redroof', 'Greystone']},
    'valley': {'Furnace Creek': 'village', 'Stovepipe Wells': 'village', 'Badwater Town': 'town', 'Panamint City': 'city', 'Ubehebe Post': 'outpost'},
    'forest': {'Oakford': 'village', 'Mill Farm': 'farm', 'Hedge Farm': 'farm', 'Ranger Lodge': 'lodge'},
}
for mid, kinds in KINDS.items():
    p = R + mid + '.json'; M = json.load(open(p))
    for a in M['areas']:
        if a['name'] in kinds: a['kind'] = kinds[a['name']]
    json.dump(M, open(p, 'w'), separators=(',', ':'))
    print(mid, [(a['name'], a.get('kind')) for a in M['areas']])
