using UnityEngine;

/// <summary>
/// Put on the ROOT of every attachment prefab. Tells the system how the attachment sits on a slot.
///
/// attachPoint: an empty child placed where the attachment touches the weapon (e.g. the underside
///              of an optic's mount, the back of a stock's tube). Its position AND rotation get
///              matched to the slot's mount point. If empty, the prefab pivot is used.
/// adsPoint:    optics and REAR iron sights only. Where the player's eye lines up (the optic's
///              eye-box center, the rear aperture). The ADS system should aim the camera at this.
///              Front sights leave it empty.
/// </summary>
public class AttachmentPrefabInfo : MonoBehaviour
{
    public Transform attachPoint;
    public Transform adsPoint;
}
