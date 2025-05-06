using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace PicoAHU
{
    public partial class Form1 : Form
    {
        // Zmienne globalne
        private string ipAddress = "192.168.10.100"; // Deff IP
        private string receivedString;
        private double tempDelta;
        private string workStatus;
        private int fanSet;
        private double tempIn;
        private double tempOut;
        private double tempExa;
        private int airValve;
        private int workMode;
        private int workRrg;
        private int errorFlag;
        private int errorCnt;
        private int i2cStat;
        private int fanSetPrec;             // lets cast it to 0 to 100%
        private bool isConnected = false;   // Flaga połączenia
        private bool isFirstTime = false;   // Flaga pierwszego polaczenia 
        private bool isSending = false;     // Hold dla wysylania 
        private string dataBuffer = "";     // Bufor na dane
        private Timer periodicTimer; // Deklaracja timera
        private TcpClient tcpClient;
        private NetworkStream networkStream;

        public Form1()
        {
            InitializeComponent();
            string filePath = "set.ini";
            periodicTimer = new Timer();
            periodicTimer.Interval = 2000; // Interwał w milisekundach (2 sekundy)
            periodicTimer.Tick += PeriodicTimer_Tick; // Podłączenie zdarzenia
            periodicTimer.Start(); // Uruchomienie timera
            LedBox1.BackColor = Color.DarkGray; // Ustawienie koloru tła na szary

            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    // Plik istnieje - odczytaj adres IP
                    ipAddress = System.IO.File.ReadAllText(filePath).Trim();

                    // Uzupełnij pole tekstowe wartością zmiennej ipAddress
                    textIpInput.Text = ipAddress;

                    labStatus.Text = $"Last IP adress: {ipAddress}";
                }
                else
                {
                    // Plik nie istnieje - nie zwracaj błędu
                    labStatus.Text = "set.ini not exist. Use default IP.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error at init: {ex.Message}");
            }
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            labStatus.Text = "Connecting ";
            string filePath = "set.ini";

            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    // Plik nie istnieje - utwórz go i zapisz adres IP
                    System.IO.File.WriteAllText(filePath, ipAddress);
                    labStatus.Text = "1st start...";
                }
                else
                {
                    // Plik istnieje - odczytaj adres IP
                    ipAddress = System.IO.File.ReadAllText(filePath).Trim();
                    labStatus.Text = $"Read: {ipAddress}";
                }

                // Wywołaj metodę SendTelnetData z wartością "@?"
                SendTelnetData("@?");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        // Funkcja do wysyłania danych przez Telnet na porcie 23
        private void SendTelnetData(string data)
        {
            try
            {
                // Jeśli połączenie nie istnieje, utwórz je
                if (tcpClient == null || !tcpClient.Connected)
                {
                    tcpClient = new TcpClient(ipAddress, 23);
                    networkStream = tcpClient.GetStream();
                    isConnected = true;
                }

                // Wyślij dane przez istniejące połączenie
                byte[] buffer = Encoding.ASCII.GetBytes(data);
                networkStream.Write(buffer, 0, buffer.Length);
                //Console.WriteLine($"buffer: {data}");
            }
            catch (Exception ex)
            {
                isConnected = false; // Ustaw flagę na false w przypadku błędu
                MessageBox.Show($"Error sending data: {ex.Message}");
            }
            // flaga 
            if (isConnected && !isFirstTime)
            {
                isFirstTime = true;
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                //Console.WriteLine("Turn on reciver:");
                _ = ReceiveTelnetDataAsync(); // Uruchom odbieranie danych w tle

            }
        }


        private async Task ReceiveTelnetDataAsync()
        {
            try
            {
                using (TcpClient client = new TcpClient(ipAddress, 23))
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[1024]; // Bufor do odbierania danych
                    while (isConnected)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            // Dodaj odebrane dane do bufora
                            dataBuffer += Encoding.ASCII.GetString(buffer, 0, bytesRead);

                            //Opóźnienie, aby poczekać na resztę danych
                            await Task.Delay(100); // 100 ms opóźnienia

                            // Sprawdź, czy bufor zawiera pełną wiadomość zakończoną CRLF
                            while (dataBuffer.Contains("\r\n"))
                            {
                                // Znajdź pozycję CRLF
                                int endOfMessage = dataBuffer.IndexOf("\r\n");

                                // Wyodrębnij pełną wiadomość
                                string completeMessage = dataBuffer.Substring(0, endOfMessage);

                                // Usuń przetworzoną wiadomość z bufora
                                dataBuffer = dataBuffer.Substring(endOfMessage + 2);

                                // Debug: Wyświetl pełną wiadomość
                               // Console.WriteLine($"Odebrane dane: {completeMessage}");

                                // Przetwórz wiadomość (np. JSON)
                                //Console.WriteLine($"buffer: {completeMessage}");
                                ProcessJsonString(completeMessage);
                             
                                // Zaktualizuj labelki o odczytane dane
                                UpdateLabels();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                isConnected = false; // Ustaw flagę na false w przypadku błędu
                MessageBox.Show($"Error receiving data: {ex.Message}");
            }
        }
        // ##################################################
        // Sest labels update
        private void UpdateLabels()
        {             // Aktualizacja etykiet na podstawie odebranych danych
            //labWM_descr.Text = $"Work Mode: {workMode}";
            labExtTemp.Text = $"{tempExa} °C";
            labOutTemp.Text = $"{tempOut} °C";
            labInTemp.Text = $"{tempIn} °C";
            labWMode_stat.Text = $"{workStatus}";
            fanSetPrec = (fanSet * 100) / 4094; // konwersja z 4094 na 0-100%
            FanSpeed.Value = fanSetPrec; // Ustawienie wartości paska postępu
            if (i2cStat == 197)
            {
                LedBox1.BackColor = Color.DarkGreen; // Ustawienie koloru tła na ciemnoslony
            }
            if (i2cStat == 199)
            {
                LedBox1.BackColor = Color.FromArgb(144, 238, 144); // Ustawienie koloru tła na zielony
            }
            //else
            //{
            //    LedBox1.BackColor = Color.Red; // Ustawienie koloru tła na czerwony
           // }
        }

        // Funkcja do przetwarzania JSON i przypisywania wartości do zmiennych globalnych
        private void ProcessJsonString(string jsonString)
        {
            try
            {
                dynamic json = JsonConvert.DeserializeObject(jsonString);

                tempDelta = (double)json.payload.Temp_Delta;
                workStatus = (string)json.payload.Work_Status;
                fanSet = (int)json.payload.Fan_Set;
                tempIn = (double)json.payload.Temp_In;
                tempOut = (double)json.payload.Temp_Out;
                tempExa = (double)json.payload.Temp_Exa;
                airValve = (int)json.payload.Air_Valve;
                workMode = (int)json.payload.Work_Mode;
                workRrg = (int)json.payload.Work_Rrg;
                errorFlag = (int)json.payload.Error_flag;
                errorCnt = (int)json.payload.error_cnt;
                i2cStat = (int)json.payload.I2c_Stat;
            }
            catch (Exception ex)
            {
              //  MessageBox.Show($"Error in JSON: {ex.Message}");
            }
        }

        private void textIpInput_TextChanged(object sender, EventArgs e)
        {
           // Pobierz tekst z pola tekstowego i przypisz go do zmiennej ipAddress
           ipAddress = ((TextBox)sender).Text;
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void label1_Click_1(object sender, EventArgs e)
        {

        }
        // ##################################################
        // ## Timer
        // ##################################################
        private void PeriodicTimer_Tick(object sender, EventArgs e)
        {
            // Wywołaj inne podprogramy tutaj
            if (isConnected)
            {
                if (!isSending)                // Sprawdz czy nie wysylamy
                {
                    SendTelnetData("@?");   // Wysylanie zapytania
                    UpdateLabels();         // Aktualizacja nalepek
                }
            }
        }
        // ########################################################
        // ## Radio button
        // ########################################################
        private void radioButton7_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;       // hold timer
                SendTelnetData("@6");
                System.Threading.Thread.Sleep(1250); // Blokujące opóźnienie 125 ms
                isSending = false;
            }
        }
        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;
                SendTelnetData("@0");
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                System.Threading.Thread.Sleep(1250); // Blokujące opóźnienie 125 ms
                isSending = false;
            }
        }
        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;
                SendTelnetData("@1");
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                System.Threading.Thread.Sleep(2500); // Blokujące opóźnienie 2500 ms
                isSending = false;
            }
        }
        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                SendTelnetData("@2");
                System.Threading.Thread.Sleep(1250); // Blokujące opóźnienie 125 ms
                isSending = false;
            }
        }
        private void radioButton4_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                SendTelnetData("@3");
                System.Threading.Thread.Sleep(1250); // Blokujące opóźnienie 125 ms
                isSending = false;
            }
        }
        private void radioButton5_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                SendTelnetData("@4");
                System.Threading.Thread.Sleep(1250); // Blokujące opóźnienie 125 ms
                isSending = false;
            }
        }
        private void radioButton6_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                SendTelnetData("@5");
                System.Threading.Thread.Sleep(1250); // Blokujące opóźnienie 125 ms
                isSending = false;
            }
        }

        private void radioButton8_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                SendTelnetData("@7");
                System.Threading.Thread.Sleep(1250); // Blokujące opóźnienie 125 ms
                isSending = false;
            }
        }

        private void radioButton9_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked) // Sprawdź, czy RadioButton jest zaznaczony
            {
                isSending = true;
                LedBox1.BackColor = Color.Yellow; // Ustawienie koloru tła na zolty
                SendTelnetData("@8");
                System.Threading.Thread.Sleep(1250); // Blokujące opóźnienie 125 ms
                isSending = false;
            }
        }
    }

}

