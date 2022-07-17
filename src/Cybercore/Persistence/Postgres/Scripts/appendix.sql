SET ROLE cybercore;

CREATE TABLE shares_vgc PARTITION OF shares FOR VALUES IN ('vgc');
CREATE TABLE shares_alps PARTITION OF shares FOR VALUES IN ('alps');
CREATE TABLE shares_btc PARTITION OF shares FOR VALUES IN ('bca');
CREATE TABLE shares_btg PARTITION OF shares FOR VALUES IN ('btg');
CREATE TABLE shares_brwn PARTITION OF shares FOR VALUES IN ('brwn');
CREATE TABLE shares_clo PARTITION OF shares FOR VALUES IN ('clo');
CREATE TABLE shares_cpu PARTITION OF shares FOR VALUES IN ('cpu');
CREATE TABLE shares_dash PARTITION OF shares FOR VALUES IN ('dash');
CREATE TABLE shares_eli PARTITION OF shares FOR VALUES IN ('eli');
CREATE TABLE shares_erg PARTITION OF shares FOR VALUES IN ('erg');
CREATE TABLE shares_eth PARTITION OF shares FOR VALUES IN ('eth');
CREATE TABLE shares_gold PARTITION OF shares FOR VALUES IN ('gold');
CREATE TABLE shares_idx PARTITION OF shares FOR VALUES IN ('idx');
CREATE TABLE shares_lana PARTITION OF shares FOR VALUES IN ('lana');
CREATE TABLE shares_ltc PARTITION OF shares FOR VALUES IN ('ltc');
CREATE TABLE shares_ntbc PARTITION OF shares FOR VALUES IN ('ntbc');
CREATE TABLE shares_obtc PARTITION OF shares FOR VALUES IN ('obtc');
CREATE TABLE shares_ppc PARTITION OF shares FOR VALUES IN ('ppc');
CREATE TABLE shares_rvl PARTITION OF shares FOR VALUES IN ('rvl');
CREATE TABLE shares_rng PARTITION OF shares FOR VALUES IN ('rng');
CREATE TABLE shares_sugar PARTITION OF shares FOR VALUES IN ('sugar');
CREATE TABLE shares_tdc PARTITION OF shares FOR VALUES IN ('tdc');
CREATE TABLE shares_tit PARTITION OF shares FOR VALUES IN ('tit');
CREATE TABLE shares_vtc PARTITION OF shares FOR VALUES IN ('vtc');
CREATE TABLE shares_ytn PARTITION OF shares FOR VALUES IN ('ytn');
CREATE TABLE shares_zec PARTITION OF shares FOR VALUES IN ('zec');