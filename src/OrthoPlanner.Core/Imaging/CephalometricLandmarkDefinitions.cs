namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// Provides the standard set of 42 cephalometric landmarks used in orthognathic surgery planning.
/// </summary>
public static class CephalometricLandmarkDefinitions
{
    public static List<CephalometricLandmark> GetAll()
    {
        var landmarks = new List<CephalometricLandmark>(42);

        // ÔöÇÔöÇ Skeletal midline (10) ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

        landmarks.Add(new CephalometricLandmark
        {
            Name = "Nasion", Abbreviation = "N",
            Description = "Midpoint of the frontonasal suture",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "Sella", Abbreviation = "S",
            Description = "Central point of the sella turcica",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "ANS", Abbreviation = "ANS",
            Description = "Most anterior median point of the anterior nasal spine",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "PNS", Abbreviation = "PNS",
            Description = "Most posterior median point of the posterior nasal spine",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "Point A", Abbreviation = "A",
            Description = "Maximum concavity on median line of maxillary alveolar process",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "Point B", Abbreviation = "B",
            Description = "Maximum concavity on median line of mandible",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "Pogonion", Abbreviation = "Pog",
            Description = "Most anterior point on medial line of mandibular symphysis",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "Menton", Abbreviation = "Me",
            Description = "Lower point of the chin on median line",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "Gnathion", Abbreviation = "Gn",
            Description = "Lower anterior point on median line of mandibular symphysis",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });
        landmarks.Add(new CephalometricLandmark
        {
            Name = "Basion", Abbreviation = "Ba",
            Description = "Most anterior point of foramen magnum",
            Category = LandmarkCategory.Skeletal, IsBilateral = false
        });

        // ÔöÇÔöÇ Bilateral skeletal (8 pairs = 16) ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

        AddBilateral(landmarks, "Porion", "Po", "Higher point of external ear canal", LandmarkCategory.Skeletal);
        AddBilateral(landmarks, "Orbitale", "Or", "Anteroinferior point of orbital rim", LandmarkCategory.Skeletal);
        AddBilateral(landmarks, "Gonion", "Go", "Most inferior, posterior, lateral point of mandibular angle", LandmarkCategory.Skeletal);
        AddBilateral(landmarks, "Frontozygomatic", "Fz", "Most medial anterior point of frontozygomatic suture", LandmarkCategory.Skeletal);
        AddBilateral(landmarks, "Zygion", "Zy", "Lateral point of zygomatic arch", LandmarkCategory.Skeletal);
        AddBilateral(landmarks, "Jugale", "J", "Lateral point of maxillary upright", LandmarkCategory.Skeletal);
        AddBilateral(landmarks, "Condylion", "Co", "Most posterior-superior point of condyle", LandmarkCategory.Skeletal);
        AddBilateral(landmarks, "Pterion", "Pt", "Intersection of round foramen and pterygomaxillary fossa", LandmarkCategory.Skeletal);

        // ÔöÇÔöÇ Dental (8 pairs = 16) ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

        AddBilateral(landmarks, "U1", "U1", "Most occlusal point of upper central incisors", LandmarkCategory.Dental);
        AddBilateral(landmarks, "U1 Root", "U1Ro", "Root apex of upper central incisors", LandmarkCategory.Dental);
        AddBilateral(landmarks, "U3", "U3", "Cusp of upper canines", LandmarkCategory.Dental);
        AddBilateral(landmarks, "U6", "U6", "Mesiobuccal cusp of upper first molars", LandmarkCategory.Dental);
        AddBilateral(landmarks, "L1", "L1", "Most occlusal point of lower central incisors", LandmarkCategory.Dental);
        AddBilateral(landmarks, "L1 Root", "L1Ro", "Root apex of lower central incisors", LandmarkCategory.Dental);
        AddBilateral(landmarks, "L3", "L3", "Cusp of lower canines", LandmarkCategory.Dental);
        AddBilateral(landmarks, "L6", "L6", "Mesiovestibular cusp of lower first molars", LandmarkCategory.Dental);

        return landmarks;
    }

    private static void AddBilateral(
        List<CephalometricLandmark> list,
        string name, string abbreviation, string description,
        LandmarkCategory category)
    {
        list.Add(new CephalometricLandmark
        {
            Name = $"{name} (R)", Abbreviation = $"{abbreviation}-R",
            Description = description,
            Category = category, IsBilateral = true
        });
        list.Add(new CephalometricLandmark
        {
            Name = $"{name} (L)", Abbreviation = $"{abbreviation}-L",
            Description = description,
            Category = category, IsBilateral = true
        });
    }
}
