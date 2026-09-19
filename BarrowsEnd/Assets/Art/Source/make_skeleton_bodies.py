"""Generates barrowsend_bodies_transparent.png: custom 16x16 same-canvas BODY layers (17px pitch, like the Kenney
Roguelike characters sheet) so the skeleton drops into the layer kit: garments/helmets/shields/weapons stack on top unchanged.
Edit the ASCII here or open the PNG in Aseprite for touch-ups. Original Kenney art is not modified."""
from PIL import Image
PAL={'A':(176,168,146),'B':(228,222,204),'C':(46,40,58),'D':(255,255,255),'E':(140,132,116)}

def blank(): return [['.']*16 for _ in range(16)]
def put(g,x,y,c):
    if 0<=x<16 and 0<=y<16: g[y][x]=c
def rect(g,x0,y0,x1,y1,c):
    for y in range(y0,y1+1):
        for x in range(x0,x1+1): put(g,x,y,c)

def skeleton(variant):
    g=blank()
    # skull
    rect(g,5,2,10,2,'A'); rect(g,4,3,11,6,'B')
    for y in range(3,7): put(g,4,y,'A'); put(g,11,y,'A')
    rect(g,5,3,10,3,'B')
    # eye sockets (2x2) + nose; variant 1 = cracked skull with a smaller, single-row socket pair
    if variant==0:
        rect(g,6,4,7,5,'C'); rect(g,9,4,10,5,'C')
    else:
        rect(g,6,5,7,5,'C'); rect(g,9,4,10,5,'C'); put(g,6,4,'A'); put(g,7,4,'A')
        put(g,8,2,'E'); put(g,8,3,'E'); put(g,9,3,'E')   # crack
    put(g,8,5,'A')
    # teeth: bone/shade alternation
    for x,c in zip(range(5,11),'BEBBEB'): put(g,x,6,c)
    # collar bone
    rect(g,3,7,12,7,'B'); put(g,3,7,'A'); put(g,12,7,'A')
    # ribcage: dark interior, bright ribs, spine
    rect(g,4,8,11,13,'C')
    for y in (8,10,12): rect(g,4,y,11,y,'B')
    for y in (9,11,13): put(g,4,y,'A'); put(g,11,y,'A')
    rect(g,7,8,8,13,'B')
    # pelvis
    rect(g,5,13,10,13,'A'); rect(g,7,13,8,13,'B')
    # arms (thin bone)
    for x in (1,2): rect(g,x,8,x,13,'B')
    for x in (13,14): rect(g,x,8,x,13,'B')
    rect(g,1,8,1,13,'A'); rect(g,14,8,14,13,'A')
    rect(g,1,14,2,14,'B'); rect(g,13,14,14,14,'B')
    # legs
    rect(g,5,14,6,15,'B'); rect(g,9,14,10,15,'B'); put(g,5,14,'A'); put(g,10,14,'A')
    rect(g,4,15,6,15,'B'); rect(g,9,15,11,15,'B'); put(g,4,15,'A'); put(g,11,15,'A')
    return g

def main():
    n=2
    im=Image.new('RGBA',(n*17-1,16),(0,0,0,0))
    for i in range(n):
        g=skeleton(i)
        for y in range(16):
            for x in range(16):
                if g[y][x]!='.': im.putpixel((i*17+x,y),PAL[g[y][x]]+(255,))
    im.save('Assets/Art/Source/barrowsend_bodies_transparent.png')
main()
