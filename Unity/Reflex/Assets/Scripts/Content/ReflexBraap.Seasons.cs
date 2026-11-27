// B.R.A.A.P. season rosters: the real fields of a season's series as built-in, read-only rosters (EDIT A COPY to change one). Each lists the
// riders in the order of the final championship standings, with the number each ran in that series that season and his bike's make (the
// card's note: the game's bikes are its own). Names, numbers and makes only: gear and bike graphics are AUTO. Collected 2026-11-16 from the
// seasons' published final standings and entry lists; a rider's number differs between series (a champion's #1) and between seasons.
// GENERATED from the collected list: one line per roster, riders as "number,name,make" separated by ';'.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reflex.Port.Content
{
    public static partial class ReflexBraap
    {
        // id | title | season | series | riders
        static readonly string[] SeasonData =
        {
            "sx450-2026|2026 AMA SUPERCROSS 450SX|2026|AMA Supercross|94,Ken Roczen,Suzuki;96,Hunter Lawrence,Honda;1,Cooper Webb,Yamaha;3,Eli Tomac,KTM;32,Justin Cooper,Yamaha;4,Chase Sexton,Kawasaki;27,Malcolm Stewart,Husqvarna;17,Joey Savatgy,Honda;26,Jorge Prado,KTM;46,Justin Hill,KTM;14,Dylan Ferrandis,Ducati;28,Christian Craig,Honda;12,Shane McElrath,Honda;36,Garrett Marchbanks,Kawasaki;45,Colt Nichols,Suzuki;7,Aaron Plessinger,KTM;21,Jason Anderson,Suzuki;41,Mitchell Harrison,Kawasaki;20,Jordon Smith,Triumph;719,Vince Friese,Kawasaki;15,Dean Wilson,Honda;62,Grant Harlan,KTM;24,RJ Hampshire,Husqvarna;51,Justin Barcia,Ducati;78,Kevin Moranz,KTM;63,Fredrik Noren,Yamaha;66,Cole Thompson,Yamaha;784,Cade Clason,Kawasaki;711,Tristan Lane,Yamaha;33,Austin Forkner,Triumph",
            "sx250w-2026|2026 AMA SUPERCROSS 250SX WEST|2026|AMA Supercross|1,Haiden Deegan,Yamaha;47,Levi Kitchen,Kawasaki;61,Max Anstie,Yamaha;34,Ryder DiFrancesco,Husqvarna;19,Maximus Vohland,Yamaha;60,Hunter Yoder,Yamaha;142,Cameron McAdoo,Kawasaki;23,Michael Mosiman,Yamaha;40,Parker Ross,Yamaha;43,Lux Turner,Yamaha;224,Joshua Varize,Kawasaki;71,Carson Mumford,KTM;57,Avery Long,KTM;42,Dilan Schwartz,Yamaha;237,Robbie Wageman,Yamaha;131,Crockett Myers,Suzuki;29,Chance Hymas,Honda;83,Justin Rodbell,Honda;70,Anthony Bourdon,Kawasaki;99,Kayden Minear,Yamaha;245,Matti Jorgensen,Honda;173,Hunter Schlosser,Honda",
            "sx250e-2026|2026 AMA SUPERCROSS 250SX EAST|2026|AMA Supercross|37,Cole Davies,Yamaha;10,Seth Hammaker,Kawasaki;58,Daxton Bennick,Husqvarna;22,Coty Schock,Yamaha;25,Nate Thrasher,Yamaha;89,Devin Simonson,Yamaha;53,Henry Miller,Kawasaki;30,Jo Shimoda,Honda;77,Derek Kelley,Kawasaki;141,Nick Romano,Kawasaki;163,Pierce Brown,Yamaha;65,Marshal Weltin,Kawasaki;148,Kyle Peters,Kawasaki;95,Luke Neese,Honda;73,Gavin Towers,Honda;101,Luke Clout,Kawasaki;82,Caden Dudney,Yamaha;49,Cullin Park,Honda;180,Landen Gordon,Yamaha;91,Izaih Clark,Honda;87,Jeremy Hand,Honda;56,Jalek Swoll,Triumph",
            "mx450-2026|2026 AMA PRO MOTOCROSS 450|2026|AMA Pro Motocross|96,Hunter Lawrence,Honda;1,Jett Lawrence,Honda;38,Haiden Deegan,Yamaha;26,Jorge Prado,KTM;24,RJ Hampshire,Husqvarna;36,Garrett Marchbanks,Kawasaki;14,Dylan Ferrandis,Ducati;2,Cooper Webb,Yamaha;31,Mikkel Haarup,Triumph;51,Justin Barcia,Ducati;20,Jordon Smith,Triumph;7,Aaron Plessinger,KTM;54,Benny Bloss,Beta;3,Eli Tomac,KTM;41,Mitchell Harrison,Kawasaki;417,Cornelius Tondel,Kawasaki;39,Valentin Guillod,Yamaha;28,Christian Craig,Honda;32,Justin Cooper,Yamaha;50,Lorenzo Locurcio,GasGas;4,Chase Sexton,Kawasaki;209,Jed Beaton,Yamaha;88,Mark Fineis,Kawasaki;63,Fredrik Noren,Yamaha;104,Lucas Coenen,KTM;222,Antonio Cairoli,Ducati;27,Malcolm Stewart,Husqvarna;62,Grant Harlan,KTM;52,Mitchell Oldenburg,Beta;939,Roan van de Moosdijk,KTM;563,Dante Oliveira,KTM;106,Stephen Rubini,Yamaha;87,Jeremy Hand,Honda;89,Devin Simonson,Yamaha;45,Colt Nichols,Suzuki;83,Justin Rodbell,Honda;76,Kyle Webster,Honda;188,Hamden Hudson,Kawasaki;69,Jack Chambers,Kawasaki;719,Vince Friese,Kawasaki",
            "mx250-2026|2026 AMA PRO MOTOCROSS 250|2026|AMA Pro Motocross|37,Cole Davies,Yamaha;13,Julien Beaumer,KTM;47,Levi Kitchen,Kawasaki;29,Chance Hymas,Honda;34,Ryder DiFrancesco,Husqvarna;99,Kayden Minear,Yamaha;71,Carson Mumford,KTM;30,Jo Shimoda,Honda;23,Michael Mosiman,Yamaha;35,Drew Adams,Kawasaki;25,Nate Thrasher,Yamaha;82,Caden Dudney,Yamaha;42,Dilan Schwartz,Yamaha;180,Landen Gordon,Yamaha;59,Casey Cochran,Husqvarna;58,Daxton Bennick,Husqvarna;43,Lux Turner,Yamaha;10,Seth Hammaker,Kawasaki;141,Nick Romano,Kawasaki;57,Avery Long,KTM;109,Sacha Coenen,KTM;65,Marshal Weltin,Kawasaki;60,Hunter Yoder,Yamaha;163,Pierce Brown,Yamaha;199,Deacon Denno,Triumph;19,Max Vohland,Yamaha;144,Brodie Connolly,Honda;40,Parker Ross,Yamaha;226,Carson Wood,Yamaha;723,Landon Gibson,Husqvarna;520,Alex Larwood,Honda;92,Enzo Temmerman,Kawasaki;77,Derek Kelley,Kawasaki;290,Francisco Garcia,Kawasaki;53,Henry Miller,Kawasaki;73,Gavin Towers,Honda;431,Ryder Malinoski,Yamaha;159,Brock Bennett,Yamaha;507,Jesson Turner,Yamaha;380,Preston Kilroy,Yamaha",
            "smx450-2026|2026 SUPERMOTOCROSS 450SMX|2026|SuperMotocross World Championship|26,Jorge Prado,KTM;38,Haiden Deegan,Yamaha;96,Hunter Lawrence,Honda;94,Ken Roczen,Suzuki;2,Cooper Webb,Yamaha;36,Garrett Marchbanks,Kawasaki;24,RJ Hampshire,Husqvarna;20,Jordon Smith,Triumph;27,Malcolm Stewart,Husqvarna;7,Aaron Plessinger,KTM;3,Eli Tomac,KTM;14,Dylan Ferrandis,Ducati;46,Justin Hill,KTM;12,Shane McElrath,Beta;28,Christian Craig,Honda;45,Colt Nichols,Suzuki;41,Mitchell Harrison,Kawasaki;31,Mikkel Haarup,Triumph;17,Joey Savatgy,Honda;719,Vince Friese,Kawasaki;1,Jett Lawrence,Honda;54,Benny Bloss,Beta",
            "smx250-2026|2026 SUPERMOTOCROSS 250SMX|2026|SuperMotocross World Championship|47,Levi Kitchen,Kawasaki;34,Ryder DiFrancesco,Husqvarna;37,Cole Davies,Yamaha;13,Julien Beaumer,KTM;180,Landen Gordon,Yamaha;25,Nate Thrasher,Yamaha;58,Daxton Bennick,Husqvarna;163,Pierce Brown,Yamaha;1,Jo Shimoda,Honda;99,Kayden Minear,Yamaha;142,Cameron McAdoo,Kawasaki;82,Caden Dudney,Yamaha;42,Dilan Schwartz,Yamaha;35,Drew Adams,Kawasaki;29,Chance Hymas,Honda;43,Lux Turner,Yamaha;19,Max Vohland,Yamaha;141,Nick Romano,Kawasaki;61,Max Anstie,Yamaha;65,Marshal Weltin,Kawasaki;89,Devin Simonson,Yamaha;10,Seth Hammaker,Kawasaki",
            "mxgp-2026|2026 FIM MXGP|2026|FIM Motocross World Championship|84,Jeffrey Herlings,Honda;1,Romain Febvre,Kawasaki;243,Tim Gajser,Yamaha;16,Tom Vialle,Honda;80,Andrea Adamo,KTM;5,Lucas Coenen,KTM;70,Ruben Fernandez,Honda;959,Maxime Renaux,Yamaha;41,Pauls Jonass,Kawasaki;74,Kay de Wolf,Husqvarna;10,Calvin Vlaanderen,Ducati;253,Jan Pancar,KTM;24,Kevin Horgmo,Honda;93,Jago Geerts,Beta;303,Alberto Forato,Fantic;83,Oriol Oliver,KTM;91,Jeremy Seewer,KTM;919,Ben Watson,Triumph;32,Brent Van Doninck,Fantic;39,Roan van de Moosdijk,KTM;517,Isak Gifting,Yamaha;4,Rick Elzinga,KTM;132,Andrea Bonacorsi,Ducati;101,Mattia Guadagnini,KTM;261,Jorgen-Matthias Talviku,Fantic;55,Maximilian Spies,KTM;87,Kevin Brumann,Husqvarna;128,Ivo Monticelli,Yamaha;53,Dylan Walsh,KTM;9,Thibault Benistant,Honda",
            "mx2-2026|2026 FIM MX2|2026|FIM Motocross World Championship|99,Guillem Farres,Triumph;8,Camden McLellan,Triumph;19,Sacha Coenen,KTM;26,Liam Everts,Husqvarna;1,Simon Langenfelder,KTM;772,Janis Reisulis,Yamaha;47,Karlis Reisulis,Yamaha;18,Valerio Lata,Honda;20,Julius Mikula,KTM;33,Kay Karssemakers,Kawasaki;83,Maxime Grau,Honda;317,Mathis Valin,Kawasaki;716,Noel Zanocz,KTM;73,Ferruccio Zanchi,Ducati;48,Jens Walvoort,KTM;408,Scott Smulders,Husqvarna;420,Andrea Rossi,KTM;172,Cas Valk,TM;456,Ollie Colmer,KTM;12,Pelle Gundersen,Husqvarna;265,Byron Dennis,KTM;98,Alessandro Gaspari,KTM;496,Alve Callemo,Husqvarna;574,Gyan Doensen,KTM;170,Valentin Kees,KTM;163,Ben Mustoe,GasGas;44,Nike Korsbeck,Triumph;938,Rodolfo Bicalho,KTM;576,Brian Gyles,KTM;97,Simone Mancini,Ducati",
            "wmx-2026|2026 FIM WMX|2026|FIM Women's Motocross World Championship|255,Daniela Guillen,GasGas;8,Kiara Fontanesi,GasGas;1,Lotte van Drunen,Yamaha;151,Courtney Duncan,Kawasaki;310,Lucy Barker,KTM;188,Shana van der Vlist,Yamaha;274,Amandine Verstappen,Yamaha;705,Malou Jakobsen,KTM;841,Nellie Fransson,Yamaha;775,Alexandra Massury,KTM;153,April Franzoni,Honda;717,Danee Gelissen,Husqvarna;172,Lynn Valk,TM;612,Mathea Seleboe,Kawasaki;587,Tyra Backstrom,GasGas",
            "sx450-2025|2025 AMA SUPERCROSS 450SX|2025|AMA Supercross|2,Cooper Webb,Yamaha;4,Chase Sexton,KTM;32,Justin Cooper,Yamaha;27,Malcolm Stewart,Husqvarna;94,Ken Roczen,Suzuki;7,Aaron Plessinger,KTM;14,Dylan Ferrandis,Honda;46,Justin Hill,KTM;12,Shane McElrath,Honda;51,Justin Barcia,GasGas;17,Joey Savatgy,Honda;21,Jason Anderson,Kawasaki;49,Mitchell Oldenburg,Beta;45,Colt Nichols,Suzuki;57,Benny Bloss,Beta;3,Eli Tomac,Yamaha;11,Kyle Chisholm,Suzuki;1,Jett Lawrence,Honda;28,Christian Craig,Yamaha;86,Mitchell Harrison,Kawasaki;96,Hunter Lawrence,Honda;15,Dean Wilson,Honda",
            "mx450-2025|2025 AMA PRO MOTOCROSS 450|2025|AMA Pro Motocross|18,Jett Lawrence,Honda;96,Hunter Lawrence,Honda;3,Eli Tomac,Yamaha;32,Justin Cooper,Yamaha;24,RJ Hampshire,Husqvarna;70,Jorge Prado,Kawasaki;27,Malcolm Stewart,Husqvarna;7,Aaron Plessinger,KTM;992,Valentin Guillod,Yamaha;51,Justin Barcia,GasGas;37,Coty Schock,Yamaha;42,Harri Kullas,Husqvarna;2,Cooper Webb,Yamaha;1,Chase Sexton,KTM;102,Benoit Paturel,Suzuki;86,Mitchell Harrison,Kawasaki;17,Joey Savatgy,Honda;74,Lorenzo Locurcio,GasGas;21,Jason Anderson,Kawasaki;14,Dylan Ferrandis,Honda;45,Colt Nichols,Suzuki;53,Romain Pape,Yamaha;43,Grant Harlan,Yamaha;35,Marshal Weltin,Yamaha;55,Henry Miller,Yamaha;69,Kyle Webster,Honda;199,John Short,Honda;58,Derek Kelley,Yamaha;12,Shane McElrath,Honda;222,Tony Cairoli,Ducati",
            "mx250-2025|2025 AMA PRO MOTOCROSS 250|2025|AMA Pro Motocross|1,Haiden Deegan,Yamaha;30,Jo Shimoda,Honda;36,Garrett Marchbanks,Kawasaki;47,Levi Kitchen,Kawasaki;16,Tom Vialle,KTM;107,Mikkel Haarup,Triumph;56,Seth Hammaker,Kawasaki;92,Maximus Vohland,Yamaha;19,Jordon Smith,Triumph;25,Ryder DiFrancesco,GasGas;98,Drew Adams,Kawasaki;44,Dilan Schwartz,Yamaha;93,Michael Mosiman,Yamaha;41,Nate Thrasher,Yamaha;23,Julien Beaumer,KTM;26,Ty Masterpool,Kawasaki;83,Austin Forkner,Triumph;302,Parker Ross,Yamaha;10,Chance Hymas,Honda;22,Jalek Swoll,Triumph;65,Lux Turner,KTM;40,Casey Cochran,GasGas;134,Avery Long,KTM;6,Jeremy Martin,Yamaha;400,Caden Dudney,Yamaha;784,Alexander Fedortsov,Yamaha;100,Cole Davies,Yamaha;201,Enzo Temmerman,Kawasaki;106,Kayden Minear,Yamaha;59,Cullin Park,Honda",
            "mxgp-2025|2025 FIM MXGP|2025|FIM Motocross World Championship|3,Romain Febvre,Kawasaki;96,Lucas Coenen,KTM;259,Glenn Coldenhoff,Fantic;70,Ruben Fernandez,Honda;84,Jeffrey Herlings,KTM;10,Calvin Vlaanderen,Yamaha;959,Maxime Renaux,Yamaha;132,Andrea Bonacorsi,Fantic;243,Tim Gajser,Honda;91,Jeremy Seewer,Ducati;253,Jan Pancar,KTM;93,Jago Geerts,Yamaha;189,Brian Bogers,Fantic;919,Ben Watson,Beta;41,Pauls Jonass,Kawasaki;517,Isak Gifting,Yamaha;101,Mattia Guadagnini,Ducati;24,Kevin Horgmo,Honda;32,Brent Van Doninck,Honda;39,Roan van de Moosdijk,KTM;226,Tom Koch,Beta;811,Adam Sterry,KTM",
            "mx2-2025|2025 FIM MX2|2025|FIM Motocross World Championship|27,Simon Langenfelder,KTM;1,Kay de Wolf,Husqvarna;80,Andrea Adamo,KTM;19,Sacha Coenen,KTM;8,Camden McLellan,Triumph;26,Liam Everts,Husqvarna;9,Thibault Benistant,Yamaha;99,Guillem Farres,Triumph;18,Valerio Lata,Honda;317,Mathis Valin,Kawasaki;47,Karlis Reisulis,Yamaha;172,Cas Valk,KTM;4,Rick Elzinga,Yamaha;51,Oriol Oliver,KTM;73,Ferruccio Zanchi,Honda;319,Quentin-Marc Prugnieres,KTM;20,Julius Mikula,TM;22,David Braceras,Honda;489,Jens Walvoort,KTM;83,Maxime Grau,KTM;33,Kay Karssemakers,Kawasaki;256,Magnus Smith,KTM",
        };

        static List<BraapRoster> s_seasons;
        /// The season rosters, newest season first, in the order above.
        public static List<BraapRoster> Seasons
        {
            get
            {
                if (s_seasons != null) return s_seasons;
                var l = new List<BraapRoster>();
                foreach (string line in SeasonData)
                {
                    var f = line.Split('|');
                    var r = new BraapRoster { id = SeasonPrefix + f[0], name = f[1], author = f[3], draw = 1,
                        note = f[2] + " season, final standings order. Numbers as run in this series." };
                    foreach (string rider in f[4].Split(';'))
                    {
                        var p = rider.Split(',');
                        r.riders.Add(new BraapRider { number = int.Parse(p[0]), name = p[1], team = p[2] });
                    }
                    l.Add(r);
                }
                return s_seasons = l;
            }
        }

        /// The seasons the built-in rosters cover, newest first.
        public static List<int> SeasonYears => SeasonData.Select(s => int.Parse(s.Split('|')[2])).Distinct().OrderByDescending(y => y).ToList();
        /// The season of a built-in roster (0: not one).
        public static int SeasonOf(BraapRoster r)
        {
            if (r == null || !r.IsSeason) return 0;
            string line = SeasonData.FirstOrDefault(s => SeasonPrefix + s.Split('|')[0] == r.id);
            return line == null ? 0 : int.Parse(line.Split('|')[2]);
        }
    }
}
