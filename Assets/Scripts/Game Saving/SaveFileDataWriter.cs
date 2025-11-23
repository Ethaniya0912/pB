using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;

public class SaveFileDataWriter
{
    public string saveDataDirectoryPath = "";
    public string saveFilename = "";

    // 존재하는 캐릭터 슬롯에 새로 저장하지 않도록 체크.
    public bool CheckToSeeIfFileExists()
    {
        if (File.Exists(Path.Combine(saveDataDirectoryPath, saveFilename)))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    // 캐릭터 세이브 파일을 지우는데 사용
    public void DeleteSaveFile()
    {
        File.Delete(Path.Combine(saveDataDirectoryPath, saveFilename));
    }

    // 뉴게임에서 새로운 세이브 파일을 만드는데 씀
    public void CreateCharacterSaveFile(CharacterSaveData characterData)
    {
        // 파일을 저장할 경로를 만듬 (머신에 존재하는 경로).
        string savePath = Path.Combine(saveDataDirectoryPath,saveFilename);

        try
        {
            // 파일이 쓰일 경로를 만듬(이미 존재하지 않을 경우)
            Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            Debug.Log("���� ������, ���� ��� : " + savePath);

            // C# 게임데이터오브젝트를 Json으로 시리얼라이즈
            string dataToStore = JsonUtility.ToJson(characterData, true);

            // 시리얼라이즈된 것을 파일로 작성.
            using (FileStream stream = new FileStream(savePath, FileMode.Create))
            {
                using (StreamWriter fileWriter = new StreamWriter(stream))
                {
                    fileWriter.Write(dataToStore);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Error whilist trying to save character data, game not saved" + savePath + "\n" + ex);
        }
    }

    // 세이브 파일을 불러오는데 사용.
    public CharacterSaveData LoadSaveFile()
    {
        CharacterSaveData characterData = null;
        // 파일을 로드하기 위하여 경로를 만듬(머신에 있는 위치)
        string loadPath = Path.Combine(saveDataDirectoryPath, saveFilename);

        if (File.Exists(loadPath))
        {
            try
            {
                string dataToLoad = "";

                using (FileStream stream = new FileStream(loadPath, FileMode.Open))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        dataToLoad = reader.ReadToEnd();
                    }
                }

                // Json 파일에서 유니티로 다시 시리얼라이즈
                characterData = JsonUtility.FromJson<CharacterSaveData>(dataToLoad);
            }
            catch (Exception ex)
            {

            }
        }

        return characterData;
    }
}
;