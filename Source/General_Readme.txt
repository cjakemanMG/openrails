# openrails# openrails 
Readme Rev. 1 January 17th, 2019
This repository is a clone of the official Open Rails repository.
Moreover it includes works in progress, and also the OR_Monogame branch.
It includes also the OR_NewYear_MG branch, which is derived from the OR_Monograme branch by merging inside a list of
features not already available in the official OR.
The OR_Monogame branch is based on the Monogame XNA emulation software (development version later than release 3.7.1).

Release notes of the OR_NewYear_MG branch:
It has been derived from the official Open Rails revision X1.3.1-6-etc , by modifying anything needed to access the Monogame software.
Moreover it includes some features not (yet) available in the Open Rails official version, that is:
- addition of track sounds in the sound debug window
- change of the font family and dimension for the digital displays in 2D cabs
- new trainspotter camera, started with Shift-4, which places itself at railroad crossings and platforms, if present
- F5 HUD scrolling
- management of up to 4 pantographs
- management of location events triggered by AI trains
- correction of calculation of station stops in a specific case
- correction of position of RP in presence of a signal.

CREDITS
A big CREDIT pertains to Peter Gulyas, who created the first Monogame version of Open Rails. 
His original work can be found here https://github.com/pzgulyas/OpenRails/tree/MonoGame .
Unfortunately I wasn't able to generate under GIT the development story starting from Peter Gulyas version to my version.
This unofficial version couldn't have been created without further following contributions:
- the whole Open Rails Development Team and Open Rails Management Team, that have generated the official Open Rails version
- the Monogame Development Team
- Carlo Santucci, who updated Peter Gulyas' work and provided some additional features
- Dennis A T, which was of great help by providing many important patches, by performing testing activities and by providing the sound debug window patch
- Mauricio (mbm_OR), who provided the F5 HUD scrolling feature
- Stijn, who provided some hints on implementation of management of 4 pantographs
- all those who provided contents for testing and pointed to malfunctions.


DISCLAIMER
No testing on a broad base of computer configurations and of contents has been done. Therefore, in addition
to the disclaimers valid also for the official Open Rails version, 
the above named persons and myself keep no responsibility, including on malfunctions, damages, losses of data or time.
It is reminded that Open Rails is distributed WITHOUT ANY WARRANTY, and without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more details.