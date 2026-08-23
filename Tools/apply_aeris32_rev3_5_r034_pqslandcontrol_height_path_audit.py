#!/usr/bin/env python3
from pathlib import Path
import base64,hashlib,re,subprocess,sys,zlib
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR034PqsLandControlHeightPathAuditObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT='AERIS32_REV3_5_R033_PTC_PURE_PROCEDURAL_DEPENDENCY_INVENTORY_SHADOW'
MARKER='AERIS32_REV3_5_R034_PQS_LANDCONTROL_HEIGHT_PATH_AUDIT_SHADOW'
OBSERVER_SHA256='12cee240c98182e93e02db5c521b231d5cd1952525fac0b440a0fc7e4af452b8'
PAYLOAD='c-rliZI!EGEfz%ucPS)!Lz?rJn?<vmwe09S%L3^9v}ONv~tq5mpFSK15QfHKMTH7sVV?qNbyzvF6LkLS)mc27YSmtB!DW{i!i$oimrCaDdoQ!M<Y8Q=a7xF^ulBOyjXaWOYACwRO_)S6O==urHpo@q%U|H`0cXPHNZb&`8k3B!t<avB6wpobSFdG?Q|R)0SoCIH#)j!qfagAGtqG@;|bJ7-gOgO5uw+*n|bPnh-o#hGRTJYI7?7Mq0b=p<apN6rILjdv6vJF43VXB22OY6Wj#I~2^O5_p^@;@1Tsl(%o!8y&=ZFbM~+D1+)teCUjJA3RRXs!mf<WMP34>X=*qi^K0Q}n5q3jpi~>KJ1=-S`m&@I^+3yY<1$&gW9AzJR`O;5(&xku$M#1)GzvnLtnaKDFZ=4}lT*1}Iw&>Zu$CYE$Je0qE*-l7W{zAW`WH(V3>aVuNp6Ms}(=UlZ^aZRFi_E{}BM^rXot6>7;@zz=F?l~;KXhvziFGyfKoLFSCqgKsNfU&Ner(mgJ_?Y0bF@TBG61KoWvd05dxvo{sSJhfdfitqye9OoPdaIO*aZ@GQ{4j)U>NzwAhV&)S8QMfRp>)6>vtmkRVwaLt!iVskNl)}fzm3kPT{sEryP4E=YQ(0EUgR#y{z^T>x$n62?JymQ#L`+lK7w!S_>uwTdKtAdV49B#l0d}A?Ap8uUa*uP4A6dF^$OcfU!!sZu>}PE`HuiqTEFSIr-~=jSUW<I*53_<NrW3Spv2a<Z)Nm0Zf$ACVLTQKB@-mw;D$Q$~oE`Y=6ZH7u6R11Z8tJ2L=b2bJHtn)4k%O`Lt8U;<0RSom}Yf+oP<q~%VP}zm#L6UDeG_FQz@6ke^Q0>2W2<$$4P)-UK1axN@Zy(JqV(D`HEfej<5)h%o3I{!0g@{NV;W)pW4uX!&mH;BOwd=t6PJC&DLeDC}bOrI^Y}T|hF;RMRWT3`uR^z6Jvyv}t6iG@j+Vw0s45>auRt?1~eTA`qkP0cm`oO!O>Ps8H*WP3wZ<;V1n%10FXWTU{GCWo?ZUVL(#l6G-8@dDD~f#lM%8th{`?acptozDfDT)-6%-Pg74*ZFHd01FoLfYD^T@j+PeG+Kf0{G!u^;A8b6OnAaKSNjGh~*Sw!Uh*En7n0Da1SRRkbGRrz^T?W|xk2W}Q$@5tM4JU8MGAJZ<@2?0ao@&5+>7B|Vc-%tR_9%Z@8Q=y0BVQ1Lh*8{yF&jf2F9!4l;1cQDsY5Ry89#Ax&T*w?G4Vw;_xH)92~Y0(?+q(-ps=54b|Cz7p~q3lG7wN?Ri&Q9{LcmB*4AqvT9%6x*;@1(_xw2TG+TDDE9%Nf^&v9dJ-FB7SYu?wiWNW=O8y?yqA&5jP4#R2#fQh)jWz9c#s6z!n}q-`i;|q<4J9Z1~}O(k;8vt|DQC5VA>C@NT)P34FZ{K!F2}6gO^cWvLv*FiUDuwsL2G;V^=S1e6V!pt^ywK9@L5fZ!$jh!5P*PYfC9$1=Q39&!P;>;D{$0Gg`R7wNcW$S0vSwsw}o98xS&t}6)~;!A<>|e2^8V0i<`nFZzN3I4*k20i>W<rPVfO8Xh*c(3nD7xYh!lLTKe9>e?z^<i#C#v@Ab0pFJ#k%hdp#clZaB8#<TWU){`hl~A%CceCh@R#ADKL^cQ8-iKeUHlVv{qL@t}t3ahMfZ;T8r#!B^6)Mc?R}h!C5L6=Ly;Sg<dsM<$91ngIyjnmoag18ORrZW$l0OwD@Y5}8ugvvcwv=oyCdiDRqo7AuHA4wvvi8pdZvRu=5tA$|1-nXtn{QW*q3;;5k03QK9nL2gb?VUwz*FH-!T_i4EvJi+j^WvgaRaDy@rF{&=b0*?ywvwZ3zP1q>zpwq6ki&LsXLE%HN6$>2rr2MB~?HSe{R`*bG%(bvWI%LX@+zK!Tg!wa7<ZQ9%K3cLqP_kK<<y-0cRY>b36J~(m^%r~Iv75T_e*rDsx&l{pfsC<+_McpO@Jg7`%-!jX7x@p_u6rRszCrsYdhf~yL$w<u;Ey%8C*UO0;?FS?j5lVUbwStvgYReP0u8(=u_2y1#S<ZSDbDH%bpdIhXBm>X3cj@u!IuGBdHw>lQ}pXlZ$F+w7ppiuGc!Yr2*+2U$sp}|x=Ft4vT?GZHPG^aKmPqs~>9uXFVZnM@A8qV*MhNksWLpLJp*)UhOlG}k8#J%r|Z*90e=mAb)rR@IIfg4&0hq(RRbdk49I4*Q^bQhIObA%|rI#|DUEg)@8D{oLi-sxdCn!n$t*jQTm7#_0NkT$-O4PW84CHZn;=jXw`R|&0~&YRtx)KNTO(DOfi!>p*ZQDMKp1%Zzef2D88Ms<5Z@a5+IC(}%Y3S4Vg|y`vCht3Sos%-le%r7<yKnR0(5UO23OoScPq-S)s!B%m#?QS$ffYis4?~eS+72YO#F+L8vM-ru$@mMQZPZ98m=O^5EfB8anN!^q%5f#Sf93|jNpC?jf%vz}>a9>6*~N^zb!h77~A}jyBOvef0Ms<yy{tQ0qo19qz)Qr^KgU4B1a#bV4N*=@z^4%2dNje%$~Jl;cSA?^!~6#p_#-rcx{yV~cL7K|X3yq~^bDEMt%DMi%nX=CByHuBgvy0WLM)sT;k(H&?#Xa8rTCN;-eSn25A~8x+e`DOW2|tA{O3ykeMRilbr~;I=G%5BDYPd^>XLiHtZWQqiS!FAKyIc+w1xO6c0NKl5U3?RN1*FBhfqa^XVc-S(FF{6b2e9`uUjg?0B2Y>fZa_r?R@8rXAtvQv)Hp8Tl=NS(7j`Ls$aB#OgzL*hD}L=Gwpd5X1w4|Ex)M{+x`T;gg9Wy@IY#cT17n)0IRl)eRFYy3<6Px5Pbb=zVK|5ZNaDjCa1+wyI=J!h23AgJaI`B^%RLGs0;eK$Be#m|MPx<9*@5mhxG%$-3L6u4CO{4z5I@f;6T24<%w9R(IYO9M|1d#zL&ICRy6hlhP~a5~0n~@{v4GfZ-kVqm*s^>H5s=a^k5kq3JNgJ36{bfZIAPXtnlUiB#a^)V9y?71ve$*+7H1xCoY8bmC0wCDlP&a2Vv(1JP%1&6&f0(hVyR>zP1Q2<XzAJZ|TC`HDz*{Br-2I&h3b~@0I*qo6sUgBDCJUi?i5q3mKQXy6((3f@FcfI2O3uep>Uq3@6?B=^STFO^qkQ}lT4qC%LZLh!Z4Og^1x^}c2r^_~PdsM_$rWMvAY1%5z0_y6dyuV<UU%V~uPqdVkWF?d<5$#m8kS{R-3BG4ajEsDaEFPf=!C!C~N}~I_{ROxIlTlSiTO2*4lz!2}vC_MGZ&{R&1YB=xTvgecvBe6N7QX!tF?$WP&Yhds!In;xQ)WOQV((CD>J`rJ8xk8Z_+jYB%jPkj1{_6%c%b7TM!Ag6y3qB`^~O7zOcO9i@V=QV@p?FSB+59_>AUc(<bz6N!G2!7_3JgH*&XKYu^Q!x$!>nWKTtV&hO@j^0l2D7yb*6*5yd`~k)>^>|MF2Ov=yNHm~z0SO(Mr}CA-qspC+aOWw`h!qA8T<(tTo|KiA5Lk1@?stjS7=2AbK1D|1%(VRk0@JYKR9`(_c#H!j)y7IPrpgA!9rsQiX9bFQL(3);sgRu8F<6OHYND_k%5b}Zy5QY8$mPkqb2+>T9t&tiDOo03z>X2Y)_%pkSt}K?m}m>n;`pe@{L^$1caWOu6p=PbiTijyuqDGI^f!J3=Gpm{eZBgfdQDQRUl#n1Gf@|V0nN(d)Vq9kLQL?Fa)BL!U;`%xX7KJXe+Ywe<H=v_z;QPbG7yVEQjjJa)L5ezbb<j6eR)u!s8d;B+G#n2y;wAn3G<bq{#|u#7uaK)6Tp84BGHA~0MLxb>W{J84CwdJW$3~aGJz^<i6FGC0n*Q!Xf%)P&O~zhTz+J$0+vln$uX0qX9zGEtF%FzkWmLf-|9Fx^Hp5Kf<`fB91YFB#XjDE%4#4+VQ2kS7)amFdci=!SSCX)x9)E&m07wi%oaFTNyMq?t}8c^0+4U?cbXtTgk^T+H)HsZu7Ev?)F_NU{~Uohk-U+rW7<HO4PtfZs^h=~VXIk?X$1t@QYgJ^P6^>^?T+8<xAj%2jW2Sj&J?IR+G_6FC0mB5qxYEez<XP0{nF>B5Ry)Y%6q|x#IRcB2Ww^bTH1NTT4DjhVwV0Q!uG^jf!)QV&oZhw!Jvl-A|AVibDOh(nE0?2)MGG|Gm!qZ>UuDsRZU4Le2?sH30iGzNQX!N+o9Vh(D5*W@eNkpku!aX$Hq$SB7yV8YZVvQY50l*$ecJm(NFmo}IZ(0iW?xq6m71LMb8{0ZAQNsCOk<EQ;;7DWuu(LAC7Kye+SOjnQc$pYFIJtlYB;1TCc5kXf(kI|?NsPz0)8k2G4cqoLxJ0E;z(9AL+EE=&d(lYCwO1V#XtZB$U?fH?V(zA#9P@r@pA>xvU3*prk<P}HhJ<f?4e^aA|M{*7uP1dg=kUxi*SwWKbZ!pe{5Wj<4YH+%I7IIn8Mfs^R|M&kPpC5jXP(yCnM8o1r}mMsiAahxrX~O%RPN1!WAb85o>=#h6Jp61QWt!TM6hch_w}&6V9&<}hWPmY?VFc0vn;%=c<T(4cC_#9oQw2OJ>1&x^<by{w|n^+>$J;#z1u=Eo@&_T5}Ol>z>g*V3Z7ngVNB(P#cLaFh{B5A5sIa={SO_OyWYxc!RPoR3O2xT#x0A`F`h|Hz5qeQVq7Vxu@>B3dhrBWc?HkZ8L`r{4-<nG8tE&Zb=4cPSL{d)Fv=3I`6cXCm3YS2n~8%SFQT#HpvybaEonOnLXznVpmaDhVu%2kC5|0x!j)SCrmz=hKOhrV$T-FK~XH=9$+MSZLd?C@f>7y}D$2+v%NoY$h?j>c;kKw5d@@K1zTgKzOX*YCQW2D~2Lw8(AEWwRtGG7lx<E<4;?c?*P61O!s7@wQy#`aN+d~l=1=vc_AAoMA_7KQN95*PtB>haIu7NQrgQ|-&0ie0<zX*Ho#g=C<Jgs(r!Wd0Eo1J;f&DF`i><vpFtv&Wi_A1BpQR3`Fsc7jhD*|stSfe(?lvWR#Yq+X@L3AkBQY`$v%Dj*=u&>O;x{!t^ld4rn4XO?5W)DU4jDm2MaOp9+0=N-N$`{+d)hMmh~7-<6!|0dT>7;e=gpdGCs0o#1c;wFih(7h!=@K8`>4fD4`G(B>-Y{=01PuF&eTA=S(E!t2cf+eb@t5Se>BOrLvNP@GJMKnZaG7~IWPUe-gHh0gY3grq~Cya3nW7`Q_h5a6Sqm*k{SKeqf8|N0jLHd3A%$+M*5mE!A^eAy@Y0!Z+^ATw~CIth>X@6A`T0e~qg+4P9)T5rPaM&pq>RsN6vHA?VEzHgwau>_0t|mS2H!ldJdIa(kvui1>CjE)P^7j&ReG&XrTGI>h;pwC=M*c_I6?%JtF@Nuw&H|sF4Y5?<0Wy1%zUgHI`U}nQG(q2Afri~Q`B77REeH$kzP1e3BX1!=4;9}g~v<6u}v&0%9M$&yi8Q{0;}DJIsZwiKH)<uaPsYKoaZvje}|xLD;JTCFHk-88NdS0y1!~W2<6gVspS6^T5;4d$Ig^PSe6wrx;hD!b*^8iP>O<3sXb)RZi<om-M9*!QC;%wbOm=tch<pbr;b(3ci_i%%bnzoL50%sxfu=~;3Y1Qhzjj?!(B~ws)SG^79<b^dpNX;IQuDlO#3kOaX?nCgtG>V2>-Nca_~J4xrI<LTUP=)=0)QO-`B@O*N+QbeNQ(%iG4)}qiir;fL(`|w7;g#M`>4?kKd2E6?8UI;8P3S^F&nQD;&{7jv>G-9m#^!)BOv*<4ESwELAhka2wh<5*g5vefp?G8uu{+|`xhg_tY0hKEs-Jj1G45=T-8moGD};uK3z$#cm`|LBT8ha8oB!$O(k13^R^%X+1v_aCg5%r|Mn1}M$N5a1So&D(ehpzwp46FtNg!{p8bN1~LN_{)!T&jF@?JjJ3e-8eKLLf4y56v%#XQ=9y^-R_{q>wNkaq4+7pN6v90fXW1`Tu{9pB8=8Ul5KXW#k5TYPQ=VqVd&!L<8F$uGApV(2I#VvB}DX6=i)teRzWVaz7XveWqMdo3!^Bc}4C$9-UrS$_S>afofgJPEr=IqU&x!7=RO$cIu@;FUFDeD8i?JN2O}r&8m0K84k>Di5vrjv@n<CeP;fMX+#I~@A4JE<4z(3I^>g@~k^xWvZrB$IfP1H=uXu8-B<PP)l!ICbMJ5D7Qw#I}lsZvG}@+rz+5cTa;wkcY+Lv=PrSngc7(eh2;1n6uL@Q?TmqZzyB5V<5KAwBHt+Q~-d<zYb7WH!5Z_Bk&-XG~@W!yXxx=r$&pv-8H^v8q}b9(xKYeAo&q89Z)}BNwf(<-|FFPIQ2I<(u~5QXZV$<~l2(n4?c%D3|I#UQ|VHQ7?E2!LwjjQ?$*D6ZuHnF7dI}nc0}uQ@ML|^C_0eB!yB4tR&&sNuFz' 
OBSERVER=zlib.decompress(base64.b85decode(PAYLOAD.encode())).decode()
if hashlib.sha256(OBSERVER.encode()).hexdigest()!=OBSERVER_SHA256:
    raise SystemExit('R034 observer payload hash mismatch')
SOURCE.parent.mkdir(parents=True,exist_ok=True)
if SOURCE.exists() and SOURCE.read_text()!=OBSERVER:
    raise SystemExit('R034 LandControl observer exists with unexpected content')
SOURCE.write_text(OBSERVER)
cs=CSPROJ.read_text()
inc='    <Compile Include="Terrain\\AERISR034PqsLandControlHeightPathAuditObserver.cs" />\n'
anchor='    <Compile Include="Terrain\\AERISR033PtcPureProceduralDependencyInventoryObserver.cs" />\n'
if inc not in cs:
    if anchor not in cs:
        raise SystemExit('R033 dependency inventory csproj anchor missing; materialize accepted R033 first')
    CSPROJ.write_text(cs.replace(anchor,anchor+inc,1))
version=VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R033 dependency inventory parent identity missing; materialize accepted R033 first')
version=version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R033 PTC PURE PROCEDURAL DEPENDENCY INVENTORY SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R034 PQS LANDCONTROL HEIGHT PATH AUDIT SHADOW'
).replace(PARENT,MARKER)
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=str(ROOT),text=True).strip()
h=hashlib.sha256()
files=sorted((ROOT/'Source/AERISFlightControl').rglob('*.cs'))+[CSPROJ]
for p in files:
    if p==VERSION: continue
    h.update(str(p.relative_to(ROOT)).encode());h.update(b'\0');h.update(p.read_bytes());h.update(b'\0')
version=re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";',
               'internal const string SourceGitSha = "'+head+'";',version)
version=re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";',
               'internal const string SourceTreeSha256 = "'+h.hexdigest()+'";',version)
VERSION.write_text(version)
print('PASS apply R034 PQSLandControl height-path audit shadow')
print('observer_sha256='+OBSERVER_SHA256)
print('head='+head)
