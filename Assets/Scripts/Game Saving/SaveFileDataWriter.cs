using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;

public class SaveFileDataWriter
{
    public string saveDataDirectoryPath = "";
    public string saveFilename = "";

    // �����ϴ� ĳ���� ���Կ� ���� �������� �ʵ��� üũ.
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

    // ĳ���� ���̺� ������ ����µ� ���.
    public void DeleteSaveFile()
    {
        File.Delete(Path.Combine(saveDataDirectoryPath, saveFilename));
    }

    // �����ӿ��� ���ο� ���̺� ������ ����µ� ��.
    public void CreateCharacterSaveFile(CharacterSaveData characterData)
    {
        // ������ ������ ��θ� ���� (�ӽſ� �����ϴ� ���).
        string savePath = Path.Combine(saveDataDirectoryPath,saveFilename);

        try
        {
            // ������ ���� ��θ� ����(�̹� ���������������)
            Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            Debug.Log("���� ������, ���� ��� : " + savePath);

            // C# ���� ������ ������Ʈ�� Json���� �ø��������
            string dataToStore = JsonUtility.ToJson(characterData, true);

            // �ø�������� �� ���� ���Ϸ� �ۼ�.
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

    // ���̺� ������ �ҷ����µ� ���.
    public CharacterSaveData LoadSaveFile()
    {
        CharacterSaveData characterData = null;
        // ������ �ε��ϱ� ���Ͽ� ��θ� ����(�ӽſ� �ִ� ��ġ)
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

                // Json ���Ͽ��� ����Ƽ�� ��ø��������
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