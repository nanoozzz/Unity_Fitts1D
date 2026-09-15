using UnityEngine;

[System.Serializable]
public class Trial
{
    public int index;
    public int group;
    public int trialInGroup;

    public float A;
    public float W;
    public float ID;

    public Trial()
    {

    }

    public Trial(
        int index,
        int group,
        int trialInGroup,
        float A,
        float W,
        float ID)
    {
        this.index = index;
        this.group = group;
        this.trialInGroup = trialInGroup;

        this.A = A;
        this.W = W;
        this.ID = ID;
    }

}
