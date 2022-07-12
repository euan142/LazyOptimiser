# Introducing the Lazy Optimiser for VRChat!
Ever get annoyed you have to go back and re-optimise an avatar because you want to add / adjust something? Want to push what your avatar can do but frustrated by the amount of time that you need to spend optimising? Well you too can be lazy (somewhat) in your avatar creation but still be (somewhat) optimised using these automated tools![^1]

## QUICK START
So, basic setup is simple, just import the package to your project via the creator companion. The editor scripts will automatically be called on avatar build.

## What does it do?
Currently the following optimisations are done (listed in order of execution)

**Remove unused gameobjects**  
Any gameobjects that are not necessary based on if they're active, animated, used in skinned meshes or dynamic bone are stripped. This is useful for stripping unused assets in general, say you have multiple outfits on the avatar structure but are only using one, the unused ones will be stripped.

**Remove unused blendshapes**  
Any blendshapes not set, animated or used for things like viseme are stripped from skinned meshes. Additionally any blendshapes that are set but never animated or used for things like viseme are baked. This is useful to reduce file size and reduce runtime expense.

**Merge meshes** (still somewhat unstable)  
Based on animations, what's active and such it will merge meshes together. This is useful as you want to minimise the amount of skinned meshes in use (with minor exceptions such as separating meshes using blendshapes).

## Requirements
- [Unity 2019 LTS (the version recommended by VRChat)](https://docs.vrchat.com/docs/current-unity-version)
- Package versions of [VRChat Base](https://github.com/vrchat/packages/tree/main/packages/com.vrchat.base) and [Avatar](https://github.com/vrchat/packages/tree/main/packages/com.vrchat.avatars) SDKs

## TODO
Here's some stuff I want to see done, either by myself or someone else. Some I'm not exactly sure how to do or if are effectively possible.

**Material combiner / Atlaser**  
Detect similar materials on meshes that can be combined accounting for materials / material slots that are animated

**Texture optimiser**  
(potentially part of the atlaser) Edit textures to only contain what's used in the UVs (with some padding of course)

**Hidden mesh detection**  
Detect occluded body mesh using lights rendering to texture or something, then remove the hidden mesh

**Conditional exclusions**  
Allow the avatar creator to set up conditions which exclude things from being removed, merged, etc

**Debug mode**  
Have an editor window allowing global disabling of various optimisation steps and outputting of the generated result as a prefab for inspection

**Bake / strip bones not actively affected**
Essentially if a bone is weighted but isn't a humanoid bone, affected by a constraint, secondary motion script, etc then it doesn't need to be there. What should be done is the weights of the bone should be given to the parent bone in these cases, then the process is run iteratively upwards until there is no useless bones.


## Thanks
This project was not done alone! The prototype of the blendshape remover was written by @FrostbyteVR, they also wrote the logic that actually merges given skinned meshes together (turns out unity didn't make it so easy)

## Contribute
Do you want to help raise the bar of optimisation in VRChat? Do you want to be lazier in your avatar creation but not sacrifice performance? Then please do feel free to help expand this project, there's so much more this could do (as mentioned in the TODO's) and I can't do this alone

## Other useful tools
[VRCAvatarActions](https://github.com/euan142/VRCAvatarActions/) - Allows you to avoid getting into animators for most things in Avatars 3.0  
  
  
[^1]: It's always important to optimise what can't be done in these tools as well, these tools can't decimate your avatar for example